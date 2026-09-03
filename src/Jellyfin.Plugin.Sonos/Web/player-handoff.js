(function (root) {
    'use strict';

    var H = {};

    /**
     * jellyfin-web's sessionPlayer.play() always sends PlayNow to
     * playbackManager.getPlayerInfo().id. Sonos transfers MUST use
     * POST /Sonos/Queue/Play (or Seek) instead, then bind the bar last.
     *
     * trySetActivePlayer('localplayer') is a no-op while Remote Control is
     * active (jellyfin-web 10.11). Local bind MUST use setActivePlayer /
     * setDefaultPlayerActive.
     */
    H.playTarget = function (playerInfo) {
        if (playerInfo && playerInfo.isLocalPlayer === false) {
            return 'remote';
        }
        return 'local';
    };

    var INCOMPATIBLE_MEDIA_TYPES = {
        Video: true,
        Photo: true,
        Book: true
    };

    var INCOMPATIBLE_ITEM_TYPES = {
        Episode: true,
        Movie: true,
        Series: true,
        Season: true,
        Trailer: true,
        Video: true,
        MusicVideo: true,
        TvChannel: true,
        Program: true,
        BoxSet: true,
        CollectionFolder: true
    };

    var COMPATIBLE_ITEM_TYPES = {
        Audio: true,
        MusicAlbum: true,
        MusicArtist: true,
        MusicGenre: true,
        AudioBook: true
    };

    H.itemMediaType = function (item) {
        if (!item || typeof item !== 'object') {
            return '';
        }
        return String(item.MediaType || item.mediaType || '');
    };

    H.itemType = function (item) {
        if (!item || typeof item !== 'object') {
            return '';
        }
        return String(item.Type || item.type || '');
    };

    H.itemIsSonosCompatible = function (item) {
        if (item == null || typeof item === 'string' || typeof item === 'number') {
            return true;
        }
        var mediaType = H.itemMediaType(item);
        if (INCOMPATIBLE_MEDIA_TYPES[mediaType]) {
            return false;
        }
        var type = H.itemType(item);
        if (INCOMPATIBLE_ITEM_TYPES[type]) {
            return false;
        }
        if (mediaType === 'Audio' || COMPATIBLE_ITEM_TYPES[type]) {
            return true;
        }
        return true;
    };

    H.itemsFromPlayOptions = function (options) {
        if (!options || typeof options !== 'object') {
            return [];
        }
        if (Array.isArray(options.items) && options.items.length) {
            return options.items;
        }
        if (Array.isArray(options.Items) && options.Items.length) {
            return options.Items;
        }
        return [];
    };

    H.idsFromPlayOptions = function (options) {
        if (!options || typeof options !== 'object') {
            return [];
        }
        var raw = options.ids || options.Ids || options.itemIds || options.ItemIds;
        if (typeof raw === 'string') {
            raw = raw.split(',');
        }
        if (Array.isArray(raw) && raw.length) {
            var ids = [];
            for (var i = 0; i < raw.length; i++) {
                if (raw[i] == null || raw[i] === '') {
                    continue;
                }
                ids.push(String(raw[i]));
            }
            return ids;
        }
        var fromItems = [];
        var items = H.itemsFromPlayOptions(options);
        for (var j = 0; j < items.length; j++) {
            var id = H.itemIdOf(items[j]);
            if (id) {
                fromItems.push(id);
            }
        }
        return fromItems;
    };

    H.playOptionsAreSonosCompatible = function (options) {
        var items = H.itemsFromPlayOptions(options);
        for (var i = 0; i < items.length; i++) {
            if (!H.itemIsSonosCompatible(items[i])) {
                return false;
            }
        }
        return true;
    };

    H.playOptionsNeedItemLookup = function (options) {
        var items = H.itemsFromPlayOptions(options);
        for (var i = 0; i < items.length; i++) {
            if (H.itemMediaType(items[i]) || H.itemType(items[i])) {
                return false;
            }
        }
        return H.idsFromPlayOptions(options).length > 0;
    };

    H.playableMediaTypesOf = function (playerInfo) {
        if (!playerInfo || typeof playerInfo !== 'object') {
            return [];
        }
        var raw = playerInfo.playableMediaTypes || playerInfo.PlayableMediaTypes;
        if (typeof raw === 'string') {
            raw = raw.split(',');
        }
        if (!Array.isArray(raw)) {
            return [];
        }
        return raw.map(function (t) {
            return String(t || '').toLowerCase();
        }).filter(Boolean);
    };

    H.isSonosBoundPlayer = function (playerInfo, coordinatorId) {
        if (H.playTarget(playerInfo) !== 'remote') {
            return false;
        }
        if (coordinatorId) {
            return true;
        }
        if (!playerInfo) {
            return false;
        }
        if (playerInfo.appName === 'Sonos' || playerInfo.AppName === 'Sonos') {
            return true;
        }
        // jellyfin-web getPlayerInfo() omits appName. Sonos sessions advertise Audio only.
        var types = H.playableMediaTypesOf(playerInfo);
        if (!types.length) {
            return false;
        }
        var hasVideo = false;
        var hasAudio = false;
        for (var i = 0; i < types.length; i++) {
            if (types[i] === 'video') {
                hasVideo = true;
            }
            if (types[i] === 'audio') {
                hasAudio = true;
            }
        }
        return hasAudio && !hasVideo;
    };

    function wrapPlayMethod(pm, name, ctx) {
        var original = pm[name];
        if (typeof original !== 'function') {
            return;
        }
        var bound = original.bind(pm);
        pm[name] = function (options) {
            if (ctx && typeof ctx.skip === 'function' && ctx.skip()) {
                return bound(options);
            }
            var info = ctx && typeof ctx.playerInfo === 'function' ? ctx.playerInfo() : null;
            var coordinatorId = ctx && typeof ctx.coordinatorId === 'function'
                ? ctx.coordinatorId()
                : (ctx && ctx.coordinatorId);
            var sonosBound = ctx && typeof ctx.isSonosBound === 'function'
                ? ctx.isSonosBound()
                : H.isSonosBoundPlayer(info, coordinatorId);
            if (H.playTarget(info) !== 'remote' || !sonosBound) {
                return bound(options);
            }
            if (!H.playOptionsAreSonosCompatible(options)) {
                return Promise.resolve(ctx && ctx.onIncompatible
                    ? ctx.onIncompatible(options, bound, name)
                    : bound(options));
            }
            if (!H.playOptionsNeedItemLookup(options) || !ctx || typeof ctx.resolveItems !== 'function') {
                return bound(options);
            }
            return Promise.resolve(ctx.resolveItems(options)).then(function (items) {
                if (H.playOptionsAreSonosCompatible({ items: items || [] })) {
                    return bound(options);
                }
                return ctx.onIncompatible(options, bound, name);
            });
        };
    }

    H.installPlayGuard = function (pm, ctx) {
        if (!pm || pm.__sonosPlayGuard) {
            return false;
        }
        pm.__sonosPlayGuard = true;
        wrapPlayMethod(pm, 'play', ctx);
        wrapPlayMethod(pm, 'queue', ctx);
        wrapPlayMethod(pm, 'queueNext', ctx);
        return true;
    };

    H.clampVolume = function (value) {
        var n = typeof value === 'number' ? value : parseInt(value, 10);
        if (isNaN(n)) {
            return null;
        }
        if (n < 0) {
            return 0;
        }
        if (n > 100) {
            return 100;
        }
        return n;
    };

    H.volumeFromQueue = function (queue) {
        if (!queue || typeof queue !== 'object') {
            return null;
        }
        var raw = queue.volume;
        if (raw == null) {
            raw = queue.Volume;
        }
        return H.clampVolume(raw);
    };

    H.mutedFromQueue = function (queue) {
        if (!queue || typeof queue !== 'object') {
            return false;
        }
        var raw = queue.muted;
        if (raw == null) {
            raw = queue.Muted;
        }
        return raw === true || raw === 'true' || raw === 1 || raw === '1';
    };

    H.setVolumeBody = function (targetId, volume) {
        return {
            targetId: targetId,
            command: 'SetVolume',
            volume: H.clampVolume(volume) || 0
        };
    };

    H.muteBody = function (targetId, muted) {
        return {
            targetId: targetId,
            command: muted ? 'Unmute' : 'Mute'
        };
    };

    H.defaultRemoteCommands = function () {
        return [
            'VolumeUp',
            'VolumeDown',
            'Mute',
            'Unmute',
            'ToggleMute',
            'SetVolume',
            'SetRepeatMode',
            'SetShuffleQueue'
        ];
    };

    H.supportedCommandsFromSession = function (session) {
        var caps = session && (session.Capabilities || session.capabilities);
        var cmds = (caps && (caps.SupportedCommands || caps.supportedCommands))
            || (session && (session.SupportedCommands || session.supportedCommands));
        if (Array.isArray(cmds) && cmds.length) {
            return cmds.slice();
        }
        return H.defaultRemoteCommands();
    };

    H.remoteTargetFromSession = function (session) {
        if (!session) {
            return null;
        }
        return {
            id: session.Id || session.id,
            name: session.DeviceName || session.deviceName,
            playerName: 'Remote Control',
            deviceName: session.DeviceName || session.deviceName,
            appName: session.Client || session.client || 'Sonos',
            isLocalPlayer: false,
            playableMediaTypes: ['Audio'],
            supportedCommands: H.supportedCommandsFromSession(session)
        };
    };

    H.itemIdOf = function (item) {
        if (item == null) {
            return '';
        }
        if (typeof item === 'string' || typeof item === 'number') {
            return String(item);
        }
        return String(item.Id || item.id || item.ItemId || item.itemId || '');
    };

    H.idsFromCaptured = function (captured) {
        captured = captured || {};
        if (captured.ids && captured.ids.length) {
            return captured.ids.slice();
        }
        var items = captured.items || [];
        var ids = [];
        for (var i = 0; i < items.length; i++) {
            var id = H.itemIdOf(items[i]);
            if (id) {
                ids.push(id);
            }
        }
        return ids;
    };

    H.hasQueue = function (captured) {
        return H.idsFromCaptured(captured).length > 0;
    };

    H.playOptionsFromCaptured = function (captured, serverId) {
        captured = captured || {};
        var ids = H.idsFromCaptured(captured);
        var opts = {
            startIndex: captured.index != null ? captured.index : 0,
            startPositionTicks: captured.ticks != null ? captured.ticks : 0
        };
        if (ids.length) {
            opts.ids = ids;
            if (serverId) {
                opts.serverId = serverId;
            }
        } else if (captured.items && captured.items.length) {
            opts.items = captured.items;
        }
        return opts;
    };

    H.sonosPlayBodyFromCaptured = function (captured, targetId) {
        captured = captured || {};
        return {
            targetId: targetId,
            itemIds: H.idsFromCaptured(captured),
            startIndex: captured.index != null ? captured.index : 0,
            startPositionTicks: captured.ticks != null ? captured.ticks : 0
        };
    };

    H.snapshotFromQueue = function (queue) {
        queue = queue || {};
        var items = queue.items || queue.Items || [];
        var index = queue.currentIndex;
        if (index == null) {
            index = queue.CurrentIndex;
        }
        if (index == null) {
            index = 0;
        }
        var ticks = queue.positionTicks;
        if (ticks == null) {
            ticks = queue.PositionTicks;
        }
        var current = items[index] || items[0];
        return {
            items: items,
            ids: items.map(H.itemIdOf).filter(Boolean),
            index: index,
            ticks: ticks || 0,
            itemId: H.itemIdOf(current)
        };
    };

    H.snapshotFromLocal = function (items, state, playlistIndex) {
        items = items || [];
        state = state || {};
        var playState = state.PlayState || state.playState || {};
        var nowPlaying = state.NowPlayingItem || state.nowPlayingItem;
        var ticks = playState.PositionTicks != null ? playState.PositionTicks
            : (playState.positionTicks != null ? playState.positionTicks : 0);
        var itemId = H.itemIdOf(nowPlaying);
        var index = playlistIndex;
        if (itemId) {
            for (var i = 0; i < items.length; i++) {
                if (H.itemIdOf(items[i]) === itemId) {
                    index = i;
                    break;
                }
            }
        }
        if (index == null || index < 0 || index >= items.length) {
            index = 0;
        }
        return {
            items: items,
            ids: items.map(H.itemIdOf).filter(Boolean),
            index: index,
            ticks: ticks || 0,
            itemId: itemId || H.itemIdOf(items[index])
        };
    };

    H.queueItemIds = function (queue) {
        var items = (queue && (queue.items || queue.Items)) || [];
        return items.map(H.itemIdOf).filter(Boolean);
    };

    H.sameQueue = function (captured, destQueue) {
        var a = H.idsFromCaptured(captured);
        var b = H.queueItemIds(destQueue);
        if (!a.length || a.length !== b.length) {
            return false;
        }
        for (var i = 0; i < a.length; i++) {
            if (a[i] !== b[i]) {
                return false;
            }
        }
        var capturedItem = (captured && captured.itemId) || a[(captured && captured.index) || 0] || '';
        var destIndex = destQueue && (destQueue.currentIndex != null ? destQueue.currentIndex : destQueue.CurrentIndex);
        if (destIndex == null) {
            destIndex = 0;
        }
        var destItems = (destQueue && (destQueue.items || destQueue.Items)) || [];
        var destItem = H.itemIdOf(destItems[destIndex]);
        return !!capturedItem && capturedItem === destItem;
    };

    H.localIsIdle = function (pm) {
        if (!pm) {
            return true;
        }
        var info = typeof pm.getPlayerInfo === 'function' ? pm.getPlayerInfo() : null;
        if (info && info.isLocalPlayer === false) {
            return true;
        }
        var player = typeof pm.getCurrentPlayer === 'function' ? pm.getCurrentPlayer() : null;
        if (!player || player.isLocalPlayer === false) {
            return true;
        }
        try {
            if (typeof pm.isPlaying === 'function') {
                return !pm.isPlaying(player);
            }
        } catch (e) {
            // Fall through to paused / currentSrc.
        }
        try {
            if (typeof player.paused === 'function' && player.paused()) {
                return true;
            }
        } catch (e2) {
            // Fall through to currentSrc.
        }
        try {
            if (typeof player.currentSrc === 'function') {
                return !player.currentSrc();
            }
        } catch (e3) {
            return false;
        }
        return true;
    };

    H.sonosQueueIsIdle = function (queue) {
        var state = queue && (queue.state || queue.State);
        return state == null || state === 'Stopped' || state === 0 || state === '0';
    };

    H.sonosQueueIsPlaying = function (queue, expectedIndex) {
        if (!queue) {
            return false;
        }
        var state = queue.state || queue.State;
        var playing = state === 'Playing' || state === 'Paused' || state === 'Transitioning'
            || state === 1 || state === 2 || state === 3;
        if (!playing) {
            return false;
        }
        if (expectedIndex == null) {
            return true;
        }
        var idx = queue.currentIndex;
        if (idx == null) {
            idx = queue.CurrentIndex;
        }
        return idx === expectedIndex;
    };

    H.plan = function (intent) {
        intent = intent || {};
        var steps = [];
        var current = intent.currentCoordinatorId || null;
        var dest = intent.coordinatorId || null;
        var hasQueue = !!intent.hasQueue;
        var ticks = intent.ticks || 0;
        var startIndex = intent.startIndex || 0;

        if (intent.destination === 'local') {
            if (current) {
                steps.push({ type: 'stopSonos', coordinatorId: current });
                steps.push({ type: 'waitSonosIdle', coordinatorId: current });
            }
            steps.push({ type: 'bindLocal' });
            if (hasQueue) {
                steps.push({ type: 'playLocal' });
            }
            return steps;
        }

        if (current && current !== dest) {
            steps.push({ type: 'stopSonos', coordinatorId: current });
            steps.push({ type: 'waitSonosIdle', coordinatorId: current });
        } else if (!intent.currentlyRemote) {
            steps.push({ type: 'stopLocal' });
            steps.push({ type: 'waitLocalIdle' });
        }

        if (hasQueue) {
            if (intent.sameQueue) {
                steps.push({
                    type: 'seekSonos',
                    coordinatorId: dest,
                    positionTicks: ticks
                });
            } else {
                steps.push({
                    type: 'playSonosApi',
                    coordinatorId: dest,
                    startIndex: startIndex,
                    ticks: ticks
                });
            }
            steps.push({
                type: 'waitSonosPlaying',
                coordinatorId: dest,
                expectedIndex: startIndex
            });
        }

        steps.push({
            type: 'bindSonos',
            coordinatorId: dest,
            sessionId: intent.sessionId
        });
        steps.push({ type: 'revealBar' });
        return steps;
    };

    H.restorePlan = function (intent) {
        intent = intent || {};
        return [
            {
                type: 'bindSonos',
                coordinatorId: intent.coordinatorId || null,
                sessionId: intent.sessionId
            },
            { type: 'revealBar' }
        ];
    };

    H.normalizeUserId = function (userId) {
        return String(userId || '').replace(/-/g, '').toLowerCase();
    };

    H.queueUserId = function (queue) {
        if (!queue || typeof queue !== 'object') {
            return '';
        }
        return H.normalizeUserId(queue.userId || queue.UserId);
    };

    H.pluginOwned = function (queue) {
        if (!queue || typeof queue !== 'object') {
            return false;
        }
        var raw = queue.pluginOwned;
        if (raw == null) {
            raw = queue.PluginOwned;
        }
        return raw === true || raw === 'true' || raw === 1 || raw === '1';
    };

    H.queueOwnedByUser = function (queue, userId) {
        var expected = H.normalizeUserId(userId);
        if (!expected || expected === '00000000000000000000000000000000') {
            return false;
        }
        if (H.queueUserId(queue) !== expected) {
            return false;
        }
        var items = (queue && (queue.items || queue.Items)) || [];
        if (!items.length) {
            return false;
        }
        var state = String((queue && (queue.state || queue.State)) || '').toLowerCase();
        if (state !== 'playing' && state !== 'paused' && state !== 'transitioning') {
            return false;
        }
        return H.pluginOwned(queue);
    };

    H.pickRestoreTarget = function (queues, userId) {
        queues = queues || [];
        var paused = null;
        for (var i = 0; i < queues.length; i++) {
            if (!H.queueOwnedByUser(queues[i], userId)) {
                continue;
            }
            var state = String((queues[i].state || queues[i].State) || '').toLowerCase();
            if (state === 'playing') {
                return queues[i];
            }
            if (!paused) {
                paused = queues[i];
            }
        }
        return paused;
    };

    H.execute = function (steps, helpers) {
        helpers = helpers || {};
        var chain = Promise.resolve();
        (steps || []).forEach(function (step) {
            chain = chain.then(function () {
                var fn = helpers[step.type];
                if (typeof fn !== 'function') {
                    throw new Error('Missing handoff helper: ' + step.type);
                }
                return fn(step);
            });
        });
        return chain;
    };

    H.isPlaybackManager = function (obj) {
        return !!(obj
            && typeof obj.getPlayerInfo === 'function'
            && typeof obj.setActivePlayer === 'function'
            && typeof obj.play === 'function'
            && (typeof obj.setDefaultPlayerActive === 'function'
                || typeof obj.trySetActivePlayer === 'function'));
    };

    H.unwrapPlaybackManager = function (exp) {
        if (H.isPlaybackManager(exp)) {
            return exp;
        }
        if (!exp || typeof exp !== 'object') {
            return null;
        }
        var keys = Object.keys(exp);
        for (var i = 0; i < keys.length; i++) {
            if (H.isPlaybackManager(exp[keys[i]])) {
                return exp[keys[i]];
            }
        }
        return null;
    };

    H.findPlaybackManager = function (req) {
        if (!req || !req.m) {
            return null;
        }
        var ids = Object.keys(req.m);
        for (var i = 0; i < ids.length; i++) {
            var factory = req.m[ids[i]];
            if (typeof factory !== 'function') {
                continue;
            }
            var src = Function.prototype.toString.call(factory);
            if (src.indexOf('trySetActivePlayer') === -1
                || src.indexOf('setDefaultPlayerActive') === -1) {
                continue;
            }
            try {
                var found = H.unwrapPlaybackManager(req(ids[i]));
                if (found) {
                    return found;
                }
            } catch (e) {
                // Try the next factory.
            }
        }
        return null;
    };

    H.findPlaybackManagerFromWebpackChunk = function (chunk) {
        if (!chunk || typeof chunk.push !== 'function') {
            return null;
        }
        var found = null;
        try {
            chunk.push([
                [Date.now()],
                {},
                function (req) {
                    found = H.findPlaybackManager(req);
                }
            ]);
        } catch (e) {
            return null;
        }
        return found;
    };

    H.nowPlayingLooksReady = function (state) {
        var item = state && (state.NowPlayingItem || state.nowPlayingItem);
        if (!item) {
            return false;
        }
        return !!(item.Name || item.name);
    };

    H.triggerPlayerEvent = function (obj, type, extraArgs) {
        if (!obj || !obj._callbacks) {
            return false;
        }
        var list = obj._callbacks[type];
        if (!list || !list.length) {
            return false;
        }
        var eventArgs = [{ type: type }].concat(extraArgs || []);
        list.slice().forEach(function (fn) {
            try {
                fn.apply(obj, eventArgs);
            } catch (e) {
                // A listener must not fail the transfer.
            }
        });
        return true;
    };

    H.nowPlayingItemFromCaptured = function (captured, serverId) {
        captured = captured || {};
        var items = captured.items || [];
        var index = captured.index != null ? captured.index : 0;
        var item = items[index] || {};
        var ids = H.idsFromCaptured(captured);
        var id = captured.itemId || H.itemIdOf(item) || ids[index] || '';
        if (!id && !(item.Name || item.name)) {
            return null;
        }
        var imageTags = item.ImageTags || item.imageTags || null;
        var primaryTag = item.PrimaryImageTag || item.primaryImageTag;
        if (primaryTag && (!imageTags || !imageTags.Primary)) {
            imageTags = Object.assign({}, imageTags || {}, { Primary: primaryTag });
        }
        return {
            Id: id,
            Name: item.Name || item.name || '',
            Album: item.Album || item.album || '',
            Artists: item.Artists || item.artists || [],
            ArtistItems: item.ArtistItems || item.artistItems,
            MediaType: 'Audio',
            Type: item.Type || item.type || 'Audio',
            ServerId: item.ServerId || item.serverId || serverId || '',
            ImageTags: imageTags,
            AlbumId: item.AlbumId || item.albumId,
            AlbumPrimaryImageTag: item.AlbumPrimaryImageTag || item.albumPrimaryImageTag,
            RunTimeTicks: item.RunTimeTicks || item.runTimeTicks || item.DurationTicks || item.durationTicks || null
        };
    };

    /**
     * Sessions often give a truthy NowPlayingItem stub (Id, no Name) right after
     * bind. jellyfin-web's bar draws nothing without Name, and artwork needs
     * ServerId plus ImageTags or AlbumPrimaryImageTag. Prefer captured names
     * and keep any session image fields that are actually populated.
     */
    H.mergeNowPlayingItem = function (sessionItem, captured, serverId) {
        var fallback = H.nowPlayingItemFromCaptured(captured, serverId);
        if (!sessionItem || typeof sessionItem !== 'object') {
            return fallback;
        }
        var item = Object.assign({}, fallback || {});
        var sessionName = sessionItem.Name || sessionItem.name;
        if (sessionName) {
            item.Name = sessionName;
        }
        var sessionAlbum = sessionItem.Album || sessionItem.album;
        if (sessionAlbum) {
            item.Album = sessionAlbum;
        }
        var sessionArtists = sessionItem.Artists || sessionItem.artists;
        if (sessionArtists && sessionArtists.length) {
            item.Artists = sessionArtists;
        }
        if (sessionItem.ArtistItems || sessionItem.artistItems) {
            item.ArtistItems = sessionItem.ArtistItems || sessionItem.artistItems;
        }
        if (sessionItem.Id || sessionItem.id) {
            item.Id = sessionItem.Id || sessionItem.id;
        }
        if (sessionItem.ImageTags || sessionItem.imageTags) {
            item.ImageTags = sessionItem.ImageTags || sessionItem.imageTags;
        }
        if (sessionItem.PrimaryImageTag || sessionItem.primaryImageTag) {
            item.ImageTags = Object.assign({}, item.ImageTags || {}, {
                Primary: sessionItem.PrimaryImageTag || sessionItem.primaryImageTag
            });
        }
        if (sessionItem.AlbumId || sessionItem.albumId) {
            item.AlbumId = sessionItem.AlbumId || sessionItem.albumId;
        }
        if (sessionItem.AlbumPrimaryImageTag || sessionItem.albumPrimaryImageTag) {
            item.AlbumPrimaryImageTag = sessionItem.AlbumPrimaryImageTag || sessionItem.albumPrimaryImageTag;
        }
        if (sessionItem.ServerId || sessionItem.serverId) {
            item.ServerId = sessionItem.ServerId || sessionItem.serverId;
        }
        if (sessionItem.RunTimeTicks || sessionItem.runTimeTicks) {
            item.RunTimeTicks = sessionItem.RunTimeTicks || sessionItem.runTimeTicks;
        }
        if (serverId && !item.ServerId) {
            item.ServerId = serverId;
        }
        item.MediaType = item.MediaType || 'Audio';
        item.Type = item.Type || 'Audio';
        if (!(item.Name || item.name)) {
            return null;
        }
        return H.applyNowPlayingAliases(item);
    };

    /**
     * jellyfin-web's now-playing bar reads PascalCase (Name, ImageTags.Primary).
     * Sessions JSON is camelCase (name, imageTags.primary). Copy aliases in place.
     */
    H.applyNowPlayingAliases = function (item) {
        if (!item || typeof item !== 'object') {
            return item;
        }
        if (!item.Name && item.name) {
            item.Name = item.name;
        }
        if (!item.Album && item.album) {
            item.Album = item.album;
        }
        if ((!item.Artists || !item.Artists.length) && item.artists && item.artists.length) {
            item.Artists = item.artists;
        }
        if (!item.ArtistItems && item.artistItems) {
            item.ArtistItems = item.artistItems;
        }
        if (!item.Id && item.id) {
            item.Id = item.id;
        }
        if (!item.ServerId && item.serverId) {
            item.ServerId = item.serverId;
        }
        if (!item.ImageTags && item.imageTags) {
            item.ImageTags = item.imageTags;
        }
        if (item.ImageTags && !item.ImageTags.Primary && item.ImageTags.primary) {
            item.ImageTags.Primary = item.ImageTags.primary;
        }
        if (!item.AlbumId && item.albumId) {
            item.AlbumId = item.albumId;
        }
        if (!item.AlbumPrimaryImageTag && item.albumPrimaryImageTag) {
            item.AlbumPrimaryImageTag = item.albumPrimaryImageTag;
        }
        if (!item.RunTimeTicks && item.runTimeTicks) {
            item.RunTimeTicks = item.runTimeTicks;
        }
        if (!item.MediaType && item.mediaType) {
            item.MediaType = item.mediaType;
        }
        if (!item.Type && item.type) {
            item.Type = item.type;
        }
        return item;
    };

    H.patchNowPlayingState = function (state, captured, serverId) {
        if (!state || typeof state !== 'object') {
            return false;
        }
        var sessionItem = state.NowPlayingItem || state.nowPlayingItem;
        var merged = H.mergeNowPlayingItem(sessionItem, captured, serverId);
        if (!merged) {
            return false;
        }
        H.applyNowPlayingAliases(merged);
        if (sessionItem && typeof sessionItem === 'object') {
            Object.assign(sessionItem, merged);
            H.applyNowPlayingAliases(sessionItem);
            state.NowPlayingItem = sessionItem;
        } else {
            state.NowPlayingItem = merged;
        }
        return true;
    };

    /**
     * SessionPlayer replaces NowPlayingItem on every Sessions websocket payload.
     * Those objects are camelCase and often lack Name. Unshift a listener so we
     * mutate the payload before the stock now-playing bar paints.
     */
    H.installNowPlayingPatch = function (player, getCaptured, getServerId) {
        if (!player || player.isLocalPlayer) {
            return false;
        }
        if (typeof getCaptured === 'function' || typeof player._sonosGetCaptured !== 'function') {
            player._sonosGetCaptured = getCaptured;
        }
        if (typeof getServerId === 'function' || typeof player._sonosGetServerId !== 'function') {
            player._sonosGetServerId = getServerId;
        }
        if (player._sonosNowPlayingPatch) {
            return true;
        }
        player._sonosNowPlayingPatch = true;
        function patch(e, state) {
            try {
                var captured = typeof player._sonosGetCaptured === 'function'
                    ? player._sonosGetCaptured()
                    : player._sonosGetCaptured;
                var serverId = typeof player._sonosGetServerId === 'function'
                    ? player._sonosGetServerId()
                    : player._sonosGetServerId;
                H.patchNowPlayingState(state, captured, serverId);
                if (player.lastPlayerData && player.lastPlayerData !== state) {
                    H.patchNowPlayingState(player.lastPlayerData, captured, serverId);
                }
            } catch (err) {
                // Must not break jellyfin-web Events.trigger / sessionPlayer.
            }
        }
        ['playbackstart', 'statechange'].forEach(function (type) {
            player._callbacks = player._callbacks || {};
            var list = player._callbacks[type] || [];
            player._callbacks[type] = [patch].concat(list);
        });
        return true;
    };

    /**
     * jellyfin-web's now-playing bar ignores bindToPlayer(..., 'init').
     * If Sessions already filled lastPlayerData before listeners attached,
     * later ticks-only updates never show the bar. Fire playbackstart
     * ourselves (same _callbacks shape as jellyfin-web Events).
     */
    H.revealNowPlayingBar = function (pm, captured, serverId) {
        if (!pm || typeof pm.getCurrentPlayer !== 'function') {
            return false;
        }
        var player = pm.getCurrentPlayer();
        if (!player || player.isLocalPlayer) {
            return false;
        }
        H.installNowPlayingPatch(player, captured, serverId);
        var state = {};
        try {
            if (typeof pm.getPlayerState === 'function') {
                state = pm.getPlayerState(player) || {};
            }
        } catch (e) {
            state = {};
        }
        if (player.lastPlayerData) {
            H.patchNowPlayingState(player.lastPlayerData, captured, serverId);
        }
        H.patchNowPlayingState(state, captured, serverId);
        var item = state.NowPlayingItem || H.mergeNowPlayingItem(null, captured, serverId);
        if (!H.nowPlayingLooksReady({ NowPlayingItem: item })) {
            return false;
        }
        H.applyNowPlayingAliases(item);
        var payload = {
            NowPlayingItem: item,
            PlayState: state.PlayState || state.playState
                || (player.lastPlayerData && player.lastPlayerData.PlayState)
                || {}
        };
        return H.triggerPlayerEvent(player, 'playbackstart', [payload])
            || H.triggerPlayerEvent(player, 'statechange', [payload]);
    };

    /**
     * Silence html5 without advancing the queue. player.stop() is treated as
     * end-of-track and jellyfin-web auto-plays the next item. playbackManager.stop()
     * sets _playNextAfterEnded = false first.
     */
    H.haltLocalPlayback = function (pm) {
        if (!pm) {
            return Promise.resolve();
        }
        var player = typeof pm.getCurrentPlayer === 'function' ? pm.getCurrentPlayer() : null;
        if (player && player.isLocalPlayer && typeof player.pause === 'function') {
            try {
                player.pause();
            } catch (e) {
                // Still try playbackManager.stop.
            }
        }
        if (player && player.isLocalPlayer && typeof pm.stop === 'function') {
            try {
                return Promise.resolve(pm.stop(player));
            } catch (e2) {
                return Promise.resolve();
            }
        }
        return Promise.resolve();
    };

    H.bindLocal = function (pm) {
        if (!pm) {
            return Promise.reject(new Error('playbackManager is not available'));
        }
        if (typeof pm.setDefaultPlayerActive === 'function') {
            pm.setDefaultPlayerActive();
        } else if (typeof pm.setActivePlayer === 'function') {
            pm.setActivePlayer('localplayer');
        } else {
            return Promise.reject(new Error('setActivePlayer is not available'));
        }
        return Promise.resolve();
    };

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = H;
    }
    root.SonosHandoff = H;
}(typeof window !== 'undefined' ? window : globalThis));
