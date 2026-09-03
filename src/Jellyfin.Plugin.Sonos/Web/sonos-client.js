(function () {
    'use strict';

    if (window.__jellyfinSonosClient) {
        return;
    }
    window.__jellyfinSonosClient = true;

    var SPEAKER_SVG =
        '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">' +
        '<path d="M17 2H7c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-5 2c1.1 0 2 .9 2 2s-.9 2-2 2-2-.9-2-2 .9-2 2-2zm0 16c-2.21 0-4-1.79-4-4s1.79-4 4-4 4 1.79 4 4-1.79 4-4 4zm0-6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z"/>' +
        '</svg>';

    var _playbackManager = null;
    var _transferBusy = false;
    var _activeCoordinatorId = null;
    var _lastMuted = false;
    var _pendingVolume = null;
    var _volumeTimer = null;

    function handoff() {
        return window.SonosHandoff;
    }

    function api() {
        return window.ApiClient || null;
    }

    function ajax(url, method, body, query) {
        var client = api();
        if (!client) {
            return Promise.reject(new Error('ApiClient is not ready'));
        }

        var opts = {
            url: query ? client.getUrl(url, query) : client.getUrl(url),
            type: method || 'GET',
            dataType: 'json'
        };
        if (body) {
            opts.data = JSON.stringify(body);
            opts.contentType = 'application/json';
        }

        return client.ajax(opts).then(function (data) {
            if (url === 'Sonos/Players') {
                return normalizeSnapshot(data);
            }
            if (url.indexOf('Sonos/Queue') === 0 || url === 'Sonos/Playstate') {
                return normalizeQueue(data);
            }
            return normalizeGroupsOnly(data);
        });
    }

    function pick(obj, camel, pascal) {
        if (!obj || typeof obj !== 'object') {
            return undefined;
        }
        if (obj[camel] !== undefined) {
            return obj[camel];
        }
        return obj[pascal];
    }

    function normalizePlayer(p) {
        return {
            id: pick(p, 'id', 'Id') || '',
            name: pick(p, 'name', 'Name') || '',
            model: pick(p, 'model', 'Model') || '',
            modelDisplayName: pick(p, 'modelDisplayName', 'ModelDisplayName') || '',
            ip: pick(p, 'ip', 'Ip') || '',
            groupId: pick(p, 'groupId', 'GroupId') || null,
            isCoordinator: !!pick(p, 'isCoordinator', 'IsCoordinator'),
            available: pick(p, 'available', 'Available') !== false
        };
    }

    function normalizeGroup(g) {
        return {
            id: pick(g, 'id', 'Id') || '',
            name: pick(g, 'name', 'Name') || '',
            coordinatorId: pick(g, 'coordinatorId', 'CoordinatorId') || '',
            memberIds: pick(g, 'memberIds', 'MemberIds') || []
        };
    }

    function normalizeSnapshot(data) {
        if (!data || typeof data !== 'object') {
            return { players: [], groups: [] };
        }

        var rawPlayers = pick(data, 'players', 'Players');
        var rawGroups = pick(data, 'groups', 'Groups');
        return {
            players: Array.isArray(rawPlayers) ? rawPlayers.map(normalizePlayer) : [],
            groups: Array.isArray(rawGroups) ? rawGroups.map(normalizeGroup) : []
        };
    }

    function normalizeQueueItem(i) {
        var id = pick(i, 'itemId', 'ItemId') || pick(i, 'id', 'Id') || '';
        return {
            Id: id,
            ItemId: id,
            Name: pick(i, 'name', 'Name') || '',
            Album: pick(i, 'album', 'Album') || '',
            Artists: pick(i, 'artists', 'Artists') || [],
            DurationTicks: pick(i, 'durationTicks', 'DurationTicks') || 0
        };
    }

    function normalizeQueue(data) {
        if (!data || typeof data !== 'object') {
            return {
                coordinatorId: '',
                state: 'Stopped',
                positionTicks: 0,
                currentIndex: 0,
                volume: 0,
                muted: false,
                userId: '',
                pluginOwned: false,
                items: []
            };
        }

        var rawItems = pick(data, 'items', 'Items');
        return {
            coordinatorId: pick(data, 'coordinatorId', 'CoordinatorId') || '',
            state: pick(data, 'state', 'State') || 'Stopped',
            positionTicks: pick(data, 'positionTicks', 'PositionTicks') || 0,
            currentIndex: pick(data, 'currentIndex', 'CurrentIndex') || 0,
            volume: pick(data, 'volume', 'Volume') || 0,
            muted: !!pick(data, 'muted', 'Muted'),
            userId: pick(data, 'userId', 'UserId') || '',
            pluginOwned: pick(data, 'pluginOwned', 'PluginOwned') === true,
            items: Array.isArray(rawItems) ? rawItems.map(normalizeQueueItem) : []
        };
    }

    function fetchQueue(targetId) {
        if (!targetId) {
            return Promise.resolve(normalizeQueue(null));
        }
        return ajax('Sonos/Queue', 'GET', null, { targetId: targetId });
    }

    function normalizeGroupsOnly(data) {
        if (!data || typeof data !== 'object') {
            return data;
        }

        var rawGroups = pick(data, 'groups', 'Groups');
        if (!Array.isArray(rawGroups)) {
            return data;
        }

        return { groups: rawGroups.map(normalizeGroup) };
    }

    function normalizeSession(s) {
        var playState = pick(s, 'playState', 'PlayState') || {};
        return {
            Id: pick(s, 'id', 'Id'),
            Client: pick(s, 'client', 'Client'),
            DeviceId: pick(s, 'deviceId', 'DeviceId'),
            DeviceName: pick(s, 'deviceName', 'DeviceName'),
            NowPlayingItem: pick(s, 'nowPlayingItem', 'NowPlayingItem'),
            NowPlayingQueue: pick(s, 'nowPlayingQueue', 'NowPlayingQueue') || [],
            PositionTicks: pick(playState, 'positionTicks', 'PositionTicks') || 0,
            Capabilities: pick(s, 'capabilities', 'Capabilities') || {},
            SupportedCommands: (function () {
                var caps = pick(s, 'capabilities', 'Capabilities') || {};
                return pick(caps, 'supportedCommands', 'SupportedCommands')
                    || pick(s, 'supportedCommands', 'SupportedCommands')
                    || [];
            }())
        };
    }

    function playback() {
        if (!_playbackManager) {
            if (window.playbackManager && handoff() && handoff().isPlaybackManager(window.playbackManager)) {
                _playbackManager = window.playbackManager;
            } else if (handoff()) {
                _playbackManager = handoff().findPlaybackManagerFromWebpackChunk(window.webpackChunk);
                if (_playbackManager) {
                    window.playbackManager = _playbackManager;
                }
            }
        }
        if (_playbackManager) {
            installPlayGuard(_playbackManager);
        }
        return _playbackManager || null;
    }

    function installPlayGuard(pm) {
        var apiHandoff = handoff();
        if (!apiHandoff || typeof apiHandoff.installPlayGuard !== 'function') {
            return;
        }
        apiHandoff.installPlayGuard(pm, {
            skip: function () {
                return _transferBusy;
            },
            playerInfo: playerInfo,
            isSonosBound: function () {
                return apiHandoff.isSonosBoundPlayer(playerInfo(), _activeCoordinatorId);
            },
            resolveItems: resolvePlayItems,
            onIncompatible: onIncompatiblePlay
        });
        installApiPlayGuard();
    }

    function resolvePlayItems(options) {
        var apiHandoff = handoff();
        var existing = apiHandoff ? apiHandoff.itemsFromPlayOptions(options) : [];
        var typed = [];
        for (var i = 0; i < existing.length; i++) {
            if (apiHandoff.itemMediaType(existing[i]) || apiHandoff.itemType(existing[i])) {
                typed.push(existing[i]);
            }
        }
        if (typed.length) {
            return Promise.resolve(typed);
        }
        var ids = apiHandoff ? apiHandoff.idsFromPlayOptions(options) : [];
        var client = api();
        if (!ids.length || !client || typeof client.getItem !== 'function') {
            return Promise.resolve(existing);
        }
        var startIndex = 0;
        if (options) {
            if (options.startIndex != null) {
                startIndex = options.startIndex;
            } else if (options.StartIndex != null) {
                startIndex = options.StartIndex;
            }
        }
        var id = ids[startIndex] || ids[0];
        return client.getItem(client.getCurrentUserId(), id).then(function (item) {
            return item ? [item] : existing;
        }).catch(function () {
            return existing;
        });
    }

    function installApiPlayGuard() {
        var client = api();
        var apiHandoff = handoff();
        if (!client || !apiHandoff || typeof client.sendPlayCommand !== 'function' || client.__sonosPlayGuard) {
            return;
        }
        var original = client.sendPlayCommand.bind(client);
        client.__sonosPlayGuard = true;
        client.sendPlayCommand = function (sessionId, remoteOptions) {
            if (_transferBusy || !apiHandoff.isSonosBoundPlayer(playerInfo(), _activeCoordinatorId)) {
                return original(sessionId, remoteOptions);
            }
            var ids = [];
            var raw = remoteOptions && (remoteOptions.ItemIds || remoteOptions.itemIds);
            if (Array.isArray(raw)) {
                ids = raw.map(String);
            } else if (raw) {
                ids = String(raw).split(',').filter(Boolean);
            }
            if (!ids.length) {
                return original(sessionId, remoteOptions);
            }
            var playOptions = {
                ids: ids,
                startIndex: (remoteOptions && (remoteOptions.StartIndex != null ? remoteOptions.StartIndex : remoteOptions.startIndex)) || 0,
                startPositionTicks: (remoteOptions && (remoteOptions.StartPositionTicks != null ? remoteOptions.StartPositionTicks : remoteOptions.startPositionTicks)) || 0,
                serverId: client.serverId && client.serverId()
            };
            return resolvePlayItems(playOptions).then(function (items) {
                if (apiHandoff.playOptionsAreSonosCompatible({ items: items })) {
                    return original(sessionId, remoteOptions);
                }
                var pm = playback();
                return onIncompatiblePlay(playOptions, function (opts) {
                    if (pm && typeof pm.play === 'function') {
                        return pm.play(opts);
                    }
                    return undefined;
                });
            });
        };
    }

    function onIncompatiblePlay(options, originalPlay) {
        return confirmPlayLocally().then(function (ok) {
            if (!ok) {
                return undefined;
            }
            closePanel();
            _transferBusy = true;
            var steps = handoff().plan({
                destination: 'local',
                currentlyRemote: true,
                currentCoordinatorId: _activeCoordinatorId,
                hasQueue: false
            });
            return handoff().execute(steps, localHandoffHelpers(emptySnapshot())).then(function () {
                return originalPlay(options);
            }).then(function (result) {
                _transferBusy = false;
                refreshActiveButtons();
                return result;
            }, function (err) {
                _transferBusy = false;
                refreshActiveButtons();
                throw err;
            });
        });
    }

    function closeConfirm() {
        var backdrop = document.querySelector('.sonos-confirm-backdrop');
        var dialog = document.querySelector('.sonos-confirm');
        if (backdrop) {
            backdrop.remove();
        }
        if (dialog) {
            dialog.remove();
        }
    }

    function confirmPlayLocally() {
        closeConfirm();
        return new Promise(function (resolve) {
            var settled = false;
            var backdrop = document.createElement('div');
            backdrop.className = 'sonos-backdrop sonos-confirm-backdrop';

            var dialog = document.createElement('div');
            dialog.className = 'sonos-dialog sonos-confirm';
            dialog.setAttribute('role', 'dialog');
            dialog.setAttribute('aria-label', 'Can\'t play this on Sonos');
            dialog.innerHTML =
                '<h2>Can\'t play this on Sonos</h2>' +
                '<p class="sonos-status">This media cannot play on Sonos speakers. Stop Sonos playback and play in this browser instead?</p>' +
                '<div class="sonos-actions">' +
                '<button type="button" class="raised emby-button sonos-confirm-cancel">Cancel</button>' +
                '<button type="button" class="raised emby-button sonos-confirm-ok">Play here</button>' +
                '</div>';

            function finish(ok) {
                if (settled) {
                    return;
                }
                settled = true;
                closeConfirm();
                resolve(ok);
            }

            backdrop.addEventListener('click', function () {
                finish(false);
            });
            dialog.addEventListener('click', function (e) {
                e.stopPropagation();
            });
            dialog.querySelector('.sonos-confirm-cancel').addEventListener('click', function () {
                finish(false);
            });
            dialog.querySelector('.sonos-confirm-ok').addEventListener('click', function () {
                finish(true);
            });

            document.body.appendChild(backdrop);
            document.body.appendChild(dialog);
            var okBtn = dialog.querySelector('.sonos-confirm-ok');
            if (okBtn && typeof okBtn.focus === 'function') {
                okBtn.focus();
            }
        });
    }

    function playerInfo() {
        var pm = playback();
        if (pm && typeof pm.getPlayerInfo === 'function') {
            var info = pm.getPlayerInfo();
            if (info) {
                return info;
            }
        }
        var btn = document.querySelector('.headerCastButton');
        var active = btn && btn.classList.contains('castButton-active');
        if (!active) {
            return { isLocalPlayer: true };
        }
        var nameEl = document.querySelector('.headerSelectedPlayer');
        var name = nameEl && (nameEl.textContent || '').trim();
        return {
            isLocalPlayer: false,
            deviceName: name,
            name: name,
            id: null,
            appName: null
        };
    }

    function waitFor(predicate, timeoutMs) {
        var start = Date.now();
        return new Promise(function (resolve, reject) {
            function tick() {
                var value = predicate();
                if (value) {
                    resolve(value);
                    return;
                }
                if (Date.now() - start > timeoutMs) {
                    reject(new Error('Timed out'));
                    return;
                }
                window.setTimeout(tick, 50);
            }
            tick();
        });
    }

    function waitForAsync(predicate, timeoutMs) {
        var start = Date.now();
        return new Promise(function (resolve, reject) {
            function tick() {
                Promise.resolve()
                    .then(predicate)
                    .then(function (value) {
                        if (value) {
                            resolve(value);
                            return;
                        }
                        if (Date.now() - start > timeoutMs) {
                            reject(new Error('Timed out'));
                            return;
                        }
                        window.setTimeout(tick, 150);
                    })
                    .catch(function () {
                        if (Date.now() - start > timeoutMs) {
                            reject(new Error('Timed out'));
                            return;
                        }
                        window.setTimeout(tick, 150);
                    });
            }
            tick();
        });
    }

    function visiblePlayers(snapshot) {
        var players = (snapshot && snapshot.players) || [];
        return players.filter(function (p) {
            return p.available !== false;
        });
    }

    function groupName(snapshot, player) {
        var groups = (snapshot && snapshot.groups) || [];
        var gid = player.groupId;
        for (var i = 0; i < groups.length; i++) {
            if (groups[i].id === gid || groups[i].coordinatorId === player.id) {
                return groups[i].name || player.name;
            }
        }
        return player.name;
    }

    function groupForPlayer(snapshot, playerId) {
        var groups = (snapshot && snapshot.groups) || [];
        for (var i = 0; i < groups.length; i++) {
            var members = groups[i].memberIds || [];
            if (groups[i].coordinatorId === playerId || members.indexOf(playerId) !== -1) {
                return groups[i];
            }
        }
        return null;
    }

    function sortedKey(ids) {
        return ids.slice().sort().join('\u0001');
    }

    function fetchSessions() {
        var client = api();
        if (!client || typeof client.getSessions !== 'function') {
            return Promise.resolve([]);
        }
        return client.getSessions({
            controllableByUserId: client.getCurrentUserId()
        }).then(function (sessions) {
            return (sessions || []).map(normalizeSession);
        }).catch(function () {
            return [];
        });
    }

    function sonosSessions(sessions) {
        return (sessions || []).filter(function (s) {
            return s.Client === 'Sonos';
        });
    }

    function activeCoordinatorId(snapshot, sessions) {
        var info = playerInfo();
        if (!info || info.isLocalPlayer) {
            return null;
        }

        var listed = sonosSessions(sessions);
        var match = listed.find(function (s) {
            return (info.id && (s.Id === info.id || s.DeviceId === info.id))
                || (info.deviceName && s.DeviceName === info.deviceName)
                || (info.name && s.DeviceName === info.name)
                || info.appName === 'Sonos' && info.deviceName && s.DeviceName === info.deviceName;
        });
        if (match) {
            return match.DeviceId;
        }

        var name = info.deviceName || info.name || '';
        if (!name) {
            return info.appName === 'Sonos' ? info.id : null;
        }

        var players = visiblePlayers(snapshot);
        for (var i = 0; i < players.length; i++) {
            var player = players[i];
            if (player.name === name || groupName(snapshot, player) === name) {
                if (player.isCoordinator) {
                    return player.id;
                }
                var group = groupForPlayer(snapshot, player.id);
                return group ? group.coordinatorId : player.id;
            }
        }

        return info.appName === 'Sonos' ? info.id : null;
    }

    function currentMemberIds(snapshot, coordinatorId) {
        if (!coordinatorId) {
            return [];
        }
        var group = groupForPlayer(snapshot, coordinatorId);
        if (group && (group.memberIds || []).length) {
            return group.memberIds.slice();
        }
        return [coordinatorId];
    }

    function isSonosActive(snapshot, sessions) {
        return !!activeCoordinatorId(snapshot, sessions);
    }

    function syncActiveButtons(snapshot, sessions) {
        var active = isSonosActive(snapshot, sessions);
        var coordinatorId = activeCoordinatorId(snapshot, sessions);
        _activeCoordinatorId = coordinatorId || null;
        var title = 'Sonos';
        if (active && snapshot) {
            var player = visiblePlayers(snapshot).find(function (p) {
                return p.id === coordinatorId;
            });
            if (player) {
                title = 'Playing on ' + groupName(snapshot, player);
            }
        }

        ['.headerSonosButton', '.btnSonos'].forEach(function (sel) {
            var btn = document.querySelector(sel);
            if (!btn) {
                return;
            }
            btn.classList.toggle('buttonActive', active);
            btn.setAttribute('aria-pressed', active ? 'true' : 'false');
            btn.title = title;
            btn.setAttribute('aria-label', title);
        });
    }

    function refreshActiveButtons() {
        Promise.all([ajax('Sonos/Players').catch(function () { return { players: [], groups: [] }; }), fetchSessions()])
            .then(function (parts) {
                syncActiveButtons(parts[0], parts[1]);
                bindVolumeControls();
                if (!_activeCoordinatorId) {
                    return;
                }
                return fetchQueue(_activeCoordinatorId).then(applyNowPlayingVolume);
            });
    }

    function applyNowPlayingVolume(queue) {
        var apiHandoff = handoff();
        if (!apiHandoff || !queue) {
            return queue;
        }

        var volume = apiHandoff.volumeFromQueue(queue);
        _lastMuted = apiHandoff.mutedFromQueue(queue);

        var slider = document.querySelector('.nowPlayingBarVolumeSlider');
        if (slider && volume != null && !slider.dragging) {
            slider.value = String(volume);
        }

        var muteBtn = document.querySelector('.nowPlayingBar .muteButton');
        if (muteBtn) {
            var icon = muteBtn.querySelector('.material-icons');
            if (icon) {
                icon.classList.toggle('volume_off', _lastMuted);
                icon.classList.toggle('volume_up', !_lastMuted);
            }
            muteBtn.title = _lastMuted ? 'Unmute' : 'Mute';
        }

        return queue;
    }

    function flushVolume() {
        _volumeTimer = null;
        var coordinatorId = _activeCoordinatorId;
        var apiHandoff = handoff();
        if (!coordinatorId || _pendingVolume == null || !apiHandoff) {
            return;
        }

        var volume = _pendingVolume;
        _pendingVolume = null;
        ajax('Sonos/Playstate', 'POST', apiHandoff.setVolumeBody(coordinatorId, volume))
            .then(applyNowPlayingVolume)
            .catch(function () {
                return undefined;
            });
    }

    function scheduleVolume(raw) {
        var apiHandoff = handoff();
        var volume = apiHandoff ? apiHandoff.clampVolume(raw) : null;
        if (volume == null) {
            return;
        }

        _pendingVolume = volume;
        if (_volumeTimer) {
            return;
        }

        _volumeTimer = window.setTimeout(flushVolume, 150);
    }

    function bindVolumeControls() {
        var slider = document.querySelector('.nowPlayingBarVolumeSlider');
        if (slider && !slider.__jellyfinSonosVolume) {
            slider.__jellyfinSonosVolume = true;
            var onInput = function (e) {
                if (!_activeCoordinatorId) {
                    return;
                }

                e.stopImmediatePropagation();
                scheduleVolume(e.target && e.target.value);
            };
            slider.addEventListener('input', onInput, true);
            slider.addEventListener('change', onInput, true);
        }

        var muteBtn = document.querySelector('.nowPlayingBar .muteButton');
        if (muteBtn && !muteBtn.__jellyfinSonosMute) {
            muteBtn.__jellyfinSonosMute = true;
            muteBtn.addEventListener('click', function (e) {
                if (!_activeCoordinatorId) {
                    return;
                }

                e.preventDefault();
                e.stopImmediatePropagation();
                var apiHandoff = handoff();
                if (!apiHandoff) {
                    return;
                }

                ajax('Sonos/Playstate', 'POST', apiHandoff.muteBody(_activeCoordinatorId, _lastMuted))
                    .then(applyNowPlayingVolume)
                    .catch(function () {
                        return undefined;
                    });
            }, true);
        }
    }

    function createSpeakerButton(extraClass) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'paper-icon-button-light ' + extraClass;
        btn.title = 'Sonos';
        btn.setAttribute('aria-label', 'Sonos');
        btn.setAttribute('aria-pressed', 'false');
        btn.innerHTML = SPEAKER_SVG;
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            openPanel();
        });
        return btn;
    }

    function insertHeaderButton() {
        var cast = document.querySelector('.headerCastButton');
        if (!cast || !cast.parentNode || document.querySelector('.headerSonosButton')) {
            return;
        }

        cast.parentNode.insertBefore(createSpeakerButton('headerButton headerSonosButton'), cast);
        refreshActiveButtons();
    }

    function insertNowPlayingButton() {
        var bar = document.querySelector('.nowPlayingBar');
        if (!bar || bar.querySelector('.btnSonos')) {
            return;
        }

        var anchor = bar.querySelector('.muteButton') || bar.querySelector('.nowPlayingBarVolumeSliderContainer');
        if (!anchor || !anchor.parentNode) {
            return;
        }

        anchor.parentNode.insertBefore(createSpeakerButton('btnSonos'), anchor);
        refreshActiveButtons();
    }

    function closePanel() {
        var backdrop = document.querySelector('.sonos-backdrop:not(.sonos-confirm-backdrop)');
        var dialog = document.querySelector('.sonos-dialog:not(.sonos-confirm)');
        if (backdrop) {
            backdrop.remove();
        }
        if (dialog) {
            dialog.remove();
        }
    }

    function setStatus(el, text, isError) {
        el.textContent = text || '';
        el.classList.toggle('sonos-error', !!isError);
    }

    function emptySnapshot() {
        return { items: [], ids: [], index: 0, ticks: 0, itemId: '' };
    }

    function captureLocalPlayback() {
        var pm = playback();
        if (!pm) {
            return Promise.resolve(emptySnapshot());
        }

        var state = null;
        try {
            state = pm.getPlayerState && pm.getPlayerState();
        } catch (e) {
            state = null;
        }

        var playlistIndex;
        try {
            if (typeof pm.getCurrentPlaylistIndex === 'function') {
                playlistIndex = pm.getCurrentPlaylistIndex();
            }
        } catch (e2) {
            playlistIndex = undefined;
        }

        var listPromise = pm.getPlaylist ? pm.getPlaylist() : Promise.resolve([]);
        return Promise.resolve(listPromise).then(function (items) {
            if (items && items.length) {
                return handoff().snapshotFromLocal(items, state || {}, playlistIndex);
            }
            var queue = state && (state.NowPlayingQueue || state.nowPlayingQueue);
            if (queue && queue.length) {
                return handoff().snapshotFromLocal(queue, state || {}, playlistIndex);
            }
            return emptySnapshot();
        });
    }

    function capturePlayback(coordinatorId) {
        if (coordinatorId) {
            return fetchQueue(coordinatorId).then(function (queue) {
                var snap = handoff().snapshotFromQueue(queue);
                if (handoff().hasQueue(snap)) {
                    return snap;
                }
                return captureLocalPlayback();
            }).catch(function () {
                return captureLocalPlayback();
            });
        }
        return captureLocalPlayback();
    }

    function playLocal(captured) {
        var apiHandoff = handoff();
        if (apiHandoff && apiHandoff.playTarget(playerInfo()) !== 'local') {
            return Promise.reject(new Error('Now-playing bar is still bound to Sonos'));
        }

        var pm = playback();
        var client = api();
        if (!pm || typeof pm.play !== 'function' || !captured) {
            return Promise.reject(new Error('playbackManager.play is not available'));
        }
        var opts = apiHandoff
            ? apiHandoff.playOptionsFromCaptured(captured, client && client.serverId && client.serverId())
            : { items: captured.items, startIndex: captured.index, startPositionTicks: captured.ticks };
        if (!opts.ids && !opts.items) {
            return Promise.resolve();
        }
        return Promise.resolve(pm.play(opts));
    }

    function playSonosApi(captured, targetId) {
        var body = handoff().sonosPlayBodyFromCaptured(captured, targetId);
        if (!body.itemIds.length) {
            return Promise.reject(new Error('Nothing is playing to move'));
        }
        return ajax('Sonos/Queue/Play', 'POST', body);
    }

    function seekSonos(targetId, positionTicks) {
        return ajax('Sonos/Playstate', 'POST', {
            targetId: targetId,
            command: 'Seek',
            positionTicks: positionTicks || 0
        }).then(function (queue) {
            var state = queue && queue.state;
            if (state === 'Paused' || state === 'Stopped') {
                return ajax('Sonos/Playstate', 'POST', {
                    targetId: targetId,
                    command: 'Play'
                });
            }
            return queue;
        });
    }

    function stopSonosCoordinator(coordinatorId) {
        return ajax('Sonos/Playstate', 'POST', {
            targetId: coordinatorId,
            command: 'Stop'
        }).catch(function () {
            return undefined;
        });
    }

    function waitLocalIdle() {
        return waitFor(function () {
            return handoff().localIsIdle(playback());
        }, 4000);
    }

    function waitSonosIdle(coordinatorId) {
        return waitForAsync(function () {
            return fetchQueue(coordinatorId).then(function (queue) {
                return handoff().sonosQueueIsIdle(queue);
            });
        }, 8000);
    }

    function waitSonosPlaying(coordinatorId, expectedIndex) {
        return waitForAsync(function () {
            return fetchQueue(coordinatorId).then(function (queue) {
                return handoff().sonosQueueIsPlaying(queue, expectedIndex);
            });
        }, 8000);
    }

    function bindLocalPlayer() {
        var pm = playback();
        if (!pm || !handoff()) {
            return Promise.reject(new Error('playbackManager is not available'));
        }
        return handoff().bindLocal(pm).then(function () {
            return waitFor(function () {
                return handoff().playTarget(playerInfo()) === 'local';
            }, 4000);
        });
    }

    function finishTransfer(dialog, snapshot, sessions, statusEl, okText) {
        _transferBusy = false;
        setStatus(statusEl, okText, false);
        refreshActiveButtons();
        window.setTimeout(closePanel, 400);
    }

    function failTransfer(dialog, snapshot, sessions, statusEl, err) {
        _transferBusy = false;
        setStatus(statusEl, errorMessage(err), true);
        updatePlayOnEnabled(dialog, snapshot, sessions);
    }

    function localHandoffHelpers(captured) {
        return {
            stopSonos: function (step) {
                return stopSonosCoordinator(step.coordinatorId);
            },
            waitSonosIdle: function (step) {
                return waitSonosIdle(step.coordinatorId);
            },
            bindLocal: bindLocalPlayer,
            playLocal: function () {
                return playLocal(captured);
            }
        };
    }

    function sonosHandoffHelpers(captured, player, snapshot) {
        return {
            stopLocal: function () {
                return handoff().haltLocalPlayback(playback());
            },
            waitLocalIdle: waitLocalIdle,
            stopSonos: function (step) {
                return stopSonosCoordinator(step.coordinatorId);
            },
            waitSonosIdle: function (step) {
                return waitSonosIdle(step.coordinatorId);
            },
            playSonosApi: function (step) {
                return playSonosApi(captured, step.coordinatorId);
            },
            seekSonos: function (step) {
                return seekSonos(step.coordinatorId, step.positionTicks);
            },
            waitSonosPlaying: function (step) {
                return waitSonosPlaying(step.coordinatorId, step.expectedIndex);
            },
            bindSonos: function () {
                return activateCastTarget(player, groupName(snapshot, player)).catch(function () {
                    return undefined;
                });
            },
            revealBar: function () {
                var latest = captured;
                function serverId() {
                    var client = api();
                    return client && typeof client.serverId === 'function' ? client.serverId() : '';
                }
                function once() {
                    return handoff().revealNowPlayingBar(playback(), latest, serverId());
                }
                var pm = playback();
                var remote = pm && typeof pm.getCurrentPlayer === 'function' ? pm.getCurrentPlayer() : null;
                handoff().installNowPlayingPatch(remote, function () {
                    return latest;
                }, serverId);
                function enrichFromLibrary() {
                    var client = api();
                    var item = handoff().nowPlayingItemFromCaptured(latest, serverId());
                    if (!client || !item || !item.Id || typeof client.getItem !== 'function') {
                        return Promise.resolve();
                    }
                    return client.getItem(client.getCurrentUserId(), item.Id).then(function (full) {
                        if (!full) {
                            return;
                        }
                        var index = latest && latest.index != null ? latest.index : 0;
                        var rows = ((latest && latest.items) || []).slice();
                        rows[index] = Object.assign({}, rows[index], full);
                        latest = Object.assign({}, latest, { items: rows });
                        if (remote && remote.lastPlayerData) {
                            handoff().patchNowPlayingState(remote.lastPlayerData, latest, serverId());
                        }
                        once();
                    }).catch(function () {
                        return undefined;
                    });
                }
                return waitFor(once, 4000).then(function () {
                    enrichFromLibrary();
                    window.setTimeout(once, 400);
                    window.setTimeout(once, 1200);
                    return undefined;
                }).catch(function () {
                    window.setTimeout(once, 400);
                    return undefined;
                });
            }
        };
    }

    function stopLocalPlayer() {
        var h = handoff();
        if (!h) {
            return;
        }
        return h.haltLocalPlayback(playback());
    }

    function activateCastTarget(player, displayName) {
        return fetchSessions().then(function (sessions) {
            var match = sonosSessions(sessions).find(function (s) {
                return s.DeviceId === player.id
                    || (s.DeviceName && displayName && s.DeviceName.indexOf(player.name) !== -1);
            });
            var pm = playback();
            var target = handoff() && match ? handoff().remoteTargetFromSession(match) : null;
            if (target && pm && typeof pm.trySetActivePlayer === 'function') {
                pm.trySetActivePlayer(target.playerName, target);
                return waitFor(function () {
                    var info = playerInfo();
                    return info && !info.isLocalPlayer && (
                        info.id === target.id
                        || info.deviceName === target.deviceName
                    );
                }, 4000);
            }
            return clickCastMenu(displayName || player.name).then(function () {
                return waitFor(function () {
                    return handoff() && handoff().playTarget(playerInfo()) === 'remote';
                }, 4000);
            });
        });
    }

    function clickCastMenu(deviceName) {
        var btn = document.querySelector('.headerCastButton');
        if (!btn) {
            return Promise.reject(new Error('Cast button not found'));
        }

        closeActiveRemoteDialog();
        btn.click();

        return waitFor(function () {
            var items = document.querySelectorAll('.actionSheetMenuItem, .listItem-button');
            for (var i = 0; i < items.length; i++) {
                var text = (items[i].textContent || '').replace(/\s+/g, ' ').trim();
                if (!text) {
                    continue;
                }
                if (text.indexOf(deviceName) !== -1 || (text.indexOf('Sonos') !== -1 && text.indexOf(deviceName.split(' + ')[0]) !== -1)) {
                    return items[i];
                }
            }
            return null;
        }, 2500).then(function (item) {
            item.click();
        });
    }

    function closeActiveRemoteDialog() {
        var disconnect = document.querySelector('.btnDisconnect');
        if (disconnect) {
            var cancel = document.querySelector('.promptDialog .btnCancel, .promptDialogButton');
            if (cancel) {
                cancel.click();
            }
        }
    }

    function applyGrouping(snapshot, sessions, checkedIds) {
        if (checkedIds.length >= 2) {
            var coordinatorId = pickCoordinator(snapshot, sessions, checkedIds);
            return ajax('Sonos/Groups', 'POST', {
                coordinatorId: coordinatorId,
                playerIds: checkedIds
            }).then(function () {
                return coordinatorId;
            });
        }

        if (checkedIds.length === 1) {
            var id = checkedIds[0];
            var group = groupForPlayer(snapshot, id);
            if (group && (group.memberIds || []).length > 1) {
                var toRemove = (group.memberIds || []).filter(function (m) {
                    return m !== id;
                });
                return ajax('Sonos/Groups/' + encodeURIComponent(group.id) + '/Members', 'POST', {
                    playerIdsToAdd: [],
                    playerIdsToRemove: toRemove
                }).then(function () {
                    return id;
                });
            }
            return Promise.resolve(id);
        }

        return Promise.resolve(null);
    }

    function pickCoordinator(snapshot, sessions, checkedIds) {
        var current = activeCoordinatorId(snapshot, sessions);
        if (current && checkedIds.indexOf(current) !== -1) {
            return current;
        }
        var first = checkedIds[0];
        var group = groupForPlayer(snapshot, first);
        if (group && checkedIds.indexOf(group.coordinatorId) !== -1) {
            return group.coordinatorId;
        }
        var player = visiblePlayers(snapshot).find(function (p) {
            return p.id === first;
        });
        return player && player.isCoordinator ? player.id : first;
    }

    function errorMessage(err) {
        if (!err) {
            return 'Request failed';
        }
        if (err.error && err.message) {
            return err.message;
        }
        if (err.responseJSON && err.responseJSON.message) {
            return err.responseJSON.message;
        }
        return err.message || 'Request failed';
    }

    function readPanelSelection(dialog) {
        var localBox = dialog.querySelector('.sonos-local');
        var checked = [];
        dialog.querySelectorAll('.sonos-list input[type="checkbox"]:checked').forEach(function (box) {
            if (box.classList.contains('sonos-local')) {
                return;
            }
            checked.push(box.value);
        });
        return {
            playLocal: !!(localBox && localBox.checked),
            checkedIds: checked
        };
    }

    function updatePlayOnEnabled(dialog, snapshot, sessions) {
        var btn = dialog.querySelector('.sonos-playon');
        if (!btn) {
            return;
        }
        var sel = readPanelSelection(dialog);
        var coordinatorId = activeCoordinatorId(snapshot, sessions);
        var playingLocal = !coordinatorId;
        var same;
        if (sel.playLocal) {
            same = playingLocal && sel.checkedIds.length === 0;
        } else if (sel.checkedIds.length === 0) {
            same = true;
        } else {
            same = !playingLocal && sortedKey(sel.checkedIds) === sortedKey(currentMemberIds(snapshot, coordinatorId));
        }
        btn.disabled = same;
    }

    function bindExclusiveChecks(dialog, snapshot, sessions) {
        var localBox = dialog.querySelector('.sonos-local');
        var speakerBoxes = dialog.querySelectorAll('.sonos-list input[type="checkbox"]:not(.sonos-local)');

        function onChange(e) {
            var target = e.target;
            if (target === localBox && localBox.checked) {
                speakerBoxes.forEach(function (box) {
                    box.checked = false;
                });
            } else if (target !== localBox && target.checked) {
                localBox.checked = false;
            }
            updatePlayOnEnabled(dialog, snapshot, sessions);
        }

        if (localBox) {
            localBox.addEventListener('change', onChange);
        }
        speakerBoxes.forEach(function (box) {
            box.addEventListener('change', onChange);
        });
    }

    function openPanel() {
        closePanel();

        var backdrop = document.createElement('div');
        backdrop.className = 'sonos-backdrop';
        backdrop.addEventListener('click', closePanel);

        var dialog = document.createElement('div');
        dialog.className = 'sonos-dialog';
        dialog.setAttribute('role', 'dialog');
        dialog.setAttribute('aria-label', 'Sonos');
        dialog.innerHTML =
            '<h2>Sonos</h2>' +
            '<p class="sonos-status">Loading speakers…</p>' +
            '<label class="sonos-row sonos-local-row">' +
            '<input type="checkbox" class="sonos-local" />' +
            '<span>Play locally</span>' +
            '</label>' +
            '<ul class="sonos-list"></ul>' +
            '<div class="sonos-actions">' +
            '<button type="button" class="raised emby-button sonos-playon">Play On</button>' +
            '<button type="button" class="raised emby-button sonos-close">Close</button>' +
            '</div>';

        dialog.querySelector('.sonos-close').addEventListener('click', closePanel);
        dialog.addEventListener('click', function (e) {
            e.stopPropagation();
        });

        document.body.appendChild(backdrop);
        document.body.appendChild(dialog);

        var statusEl = dialog.querySelector('.sonos-status');
        var listEl = dialog.querySelector('.sonos-list');
        var playBtn = dialog.querySelector('.sonos-playon');
        var snapshot = null;
        var sessions = [];

        playBtn.addEventListener('click', function () {
            if (!snapshot || playBtn.disabled || _transferBusy) {
                return;
            }
            var sel = readPanelSelection(dialog);
            playBtn.disabled = true;

            if (sel.playLocal) {
                _transferBusy = true;
                var previousLocal = activeCoordinatorId(snapshot, sessions);
                capturePlayback(previousLocal)
                    .then(function (captured) {
                        var hasQueue = handoff().hasQueue(captured);
                        setStatus(statusEl, hasQueue ? 'Moving playback to this device…' : 'Disconnecting…', false);
                        return handoff().execute(handoff().plan({
                            destination: 'local',
                            currentlyRemote: !!previousLocal,
                            currentCoordinatorId: previousLocal,
                            hasQueue: hasQueue,
                            startIndex: captured.index,
                            ticks: captured.ticks
                        }), localHandoffHelpers(captured));
                    })
                    .then(function () {
                        finishTransfer(dialog, snapshot, sessions, statusEl, 'Playing locally');
                    })
                    .catch(function (err) {
                        failTransfer(dialog, snapshot, sessions, statusEl, err);
                    });
                return;
            }

            if (sel.checkedIds.length === 0) {
                setStatus(statusEl, 'Select a speaker or Play locally', true);
                updatePlayOnEnabled(dialog, snapshot, sessions);
                return;
            }

            var coordinatorId = pickCoordinator(snapshot, sessions, sel.checkedIds);
            var player = visiblePlayers(snapshot).find(function (p) {
                return p.id === coordinatorId;
            }) || visiblePlayers(snapshot).find(function (p) {
                return p.id === sel.checkedIds[0];
            });
            if (!player) {
                setStatus(statusEl, 'Could not resolve that speaker', true);
                updatePlayOnEnabled(dialog, snapshot, sessions);
                return;
            }

            var previous = activeCoordinatorId(snapshot, sessions);
            var alreadyOn = previous === coordinatorId;
            var movedQueue = false;
            _transferBusy = true;
            setStatus(statusEl, alreadyOn ? 'Updating speakers…' : 'Connecting…', false);

            capturePlayback(previous)
                .then(function (captured) {
                    var hasQueue = handoff().hasQueue(captured);
                    movedQueue = hasQueue;
                    if (!alreadyOn) {
                        setStatus(statusEl, hasQueue ? 'Switching playback…' : 'Connecting…', false);
                    }
                    return applyGrouping(snapshot, sessions, sel.checkedIds).then(function (resolved) {
                        coordinatorId = resolved || coordinatorId;
                        player = visiblePlayers(snapshot).find(function (p) {
                            return p.id === coordinatorId;
                        }) || player;
                        if (alreadyOn) {
                            return ajax('Sonos/Players').then(function (next) {
                                snapshot = next;
                            });
                        }
                        var match = sonosSessions(sessions).find(function (s) {
                            return s.DeviceId === coordinatorId || s.DeviceId === player.id;
                        });
                        return fetchQueue(coordinatorId).catch(function () {
                            return null;
                        }).then(function (destQueue) {
                            return handoff().execute(handoff().plan({
                                destination: 'sonos',
                                coordinatorId: coordinatorId,
                                sessionId: match && match.Id,
                                currentlyRemote: !!previous,
                                currentCoordinatorId: previous,
                                hasQueue: hasQueue,
                                sameQueue: hasQueue && !!(destQueue && handoff().sameQueue(captured, destQueue)),
                                startIndex: captured.index,
                                ticks: captured.ticks
                            }), sonosHandoffHelpers(captured, player, snapshot));
                        });
                    });
                })
                .then(function () {
                    var room = groupName(snapshot, player);
                    finishTransfer(
                        dialog,
                        snapshot,
                        sessions,
                        statusEl,
                        movedQueue ? 'Now playing on ' + room : 'Connected to ' + room
                    );
                })
                .catch(function (err) {
                    failTransfer(dialog, snapshot, sessions, statusEl, err);
                });
        });

        Promise.all([ajax('Sonos/Players'), fetchSessions()])
            .then(function (parts) {
                snapshot = parts[0];
                sessions = parts[1];
                renderList(dialog, snapshot, sessions, statusEl, listEl);
            })
            .catch(function (err) {
                listEl.innerHTML = '';
                setStatus(
                    statusEl,
                    errorMessage(err) + ' Check Seed player IPs and Published base URL in Dashboard → Plugins → Sonos.',
                    true
                );
            });
    }

    function renderList(dialog, snapshot, sessions, statusEl, listEl) {
        var players = visiblePlayers(snapshot);
        listEl.innerHTML = '';
        var coordinatorId = activeCoordinatorId(snapshot, sessions);
        var memberIds = currentMemberIds(snapshot, coordinatorId);
        var localBox = dialog.querySelector('.sonos-local');
        localBox.checked = !coordinatorId;

        if (players.length === 0) {
            var empty = document.createElement('p');
            empty.className = 'sonos-empty';
            empty.textContent = 'No speakers found. Set Seed player IPs and Published base URL in Dashboard → Plugins → Sonos.';
            listEl.appendChild(empty);
            setStatus(statusEl, 'No speakers discovered', false);
            bindExclusiveChecks(dialog, snapshot, sessions);
            updatePlayOnEnabled(dialog, snapshot, sessions);
            return;
        }

        if (coordinatorId) {
            var activePlayer = players.find(function (p) {
                return p.id === coordinatorId;
            });
            setStatus(statusEl, 'Playing on ' + (activePlayer ? groupName(snapshot, activePlayer) : 'Sonos'), false);
        } else {
            setStatus(statusEl, 'Playing locally', false);
        }

        players.forEach(function (player) {
            var li = document.createElement('li');
            li.className = 'sonos-row';

            var label = document.createElement('label');
            var box = document.createElement('input');
            box.type = 'checkbox';
            box.value = player.id;
            box.checked = !localBox.checked && memberIds.indexOf(player.id) !== -1;

            var name = document.createElement('span');
            name.textContent = player.name + (player.modelDisplayName ? ' · ' + player.modelDisplayName : '');

            label.appendChild(box);
            label.appendChild(name);
            li.appendChild(label);
            listEl.appendChild(li);
        });

        bindExclusiveChecks(dialog, snapshot, sessions);
        updatePlayOnEnabled(dialog, snapshot, sessions);
    }

    function coordinatorIds(snapshot) {
        var ids = [];
        function add(id) {
            if (!id || ids.indexOf(id) !== -1) {
                return;
            }
            ids.push(id);
        }
        var groups = (snapshot && snapshot.groups) || [];
        groups.forEach(function (g) {
            add(g.coordinatorId);
        });
        visiblePlayers(snapshot).forEach(function (p) {
            if (p.isCoordinator) {
                add(p.id);
            }
        });
        if (!ids.length) {
            visiblePlayers(snapshot).forEach(function (p) {
                add(p.id);
            });
        }
        return ids;
    }

    function restoreOnBoot() {
        var h = handoff();
        if (!h) {
            return;
        }

        waitFor(function () {
            var client = api();
            var pm = playback();
            return client
                && typeof client.getCurrentUserId === 'function'
                && client.getCurrentUserId()
                && pm
                && typeof pm.trySetActivePlayer === 'function';
        }, 15000).then(function () {
            if (_transferBusy || h.playTarget(playerInfo()) === 'remote' || !h.localIsIdle(playback())) {
                return;
            }
            var userId = api().getCurrentUserId();
            return ajax('Sonos/Players').then(function (snapshot) {
                var ids = coordinatorIds(snapshot);
                if (!ids.length) {
                    return;
                }
                return Promise.all(ids.map(function (id) {
                    return fetchQueue(id).catch(function () {
                        return null;
                    });
                })).then(function (queues) {
                    var target = h.pickRestoreTarget(queues.filter(Boolean), userId);
                    if (!target || !target.coordinatorId) {
                        return;
                    }
                    var player = visiblePlayers(snapshot).find(function (p) {
                        return p.id === target.coordinatorId;
                    }) || { id: target.coordinatorId, name: '', isCoordinator: true };
                    _transferBusy = true;
                    return fetchSessions().then(function (sessions) {
                        var match = sonosSessions(sessions).find(function (s) {
                            return s.DeviceId === player.id;
                        });
                        return h.execute(h.restorePlan({
                            coordinatorId: player.id,
                            sessionId: match && match.Id
                        }), sonosHandoffHelpers(h.snapshotFromQueue(target), player, snapshot));
                    }).then(function () {
                        _transferBusy = false;
                    }, function () {
                        _transferBusy = false;
                    });
                });
            });
        }).catch(function () {
            // Music stays on the speaker if bind is not ready yet.
        });
    }

    function tick() {
        insertHeaderButton();
        insertNowPlayingButton();
        bindVolumeControls();
        playback();
        installApiPlayGuard();
    }

    var observer = new MutationObserver(tick);
    if (document.body) {
        observer.observe(document.body, { childList: true, subtree: true });
        tick();
    } else {
        document.addEventListener('DOMContentLoaded', function () {
            observer.observe(document.body, { childList: true, subtree: true });
            tick();
        });
    }

    window.setInterval(refreshActiveButtons, 2000);
    restoreOnBoot();
})();
