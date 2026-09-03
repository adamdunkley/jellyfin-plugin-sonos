'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const H = require('../../src/Jellyfin.Plugin.Sonos/Web/player-handoff.js');

test('play() follows the bound player: remote Play To still owns the now-playing bar', () => {
    assert.equal(H.playTarget({ isLocalPlayer: false, id: 'session-1' }), 'remote');
    assert.equal(H.playTarget({ isLocalPlayer: true }), 'local');
    assert.equal(H.playTarget(null), 'local');
    assert.equal(H.playTarget(undefined), 'local');
});

test('video and collection types are not Sonos-compatible; audio and music folders are', () => {
    assert.equal(H.itemIsSonosCompatible({ Type: 'Episode', MediaType: 'Video' }), false);
    assert.equal(H.itemIsSonosCompatible({ Type: 'Movie' }), false);
    assert.equal(H.itemIsSonosCompatible({ Type: 'Series' }), false);
    assert.equal(H.itemIsSonosCompatible({ MediaType: 'Video' }), false);
    assert.equal(H.itemIsSonosCompatible({ Type: 'Audio', MediaType: 'Audio' }), true);
    assert.equal(H.itemIsSonosCompatible({ Type: 'MusicAlbum' }), true);
    assert.equal(H.itemIsSonosCompatible({ Type: 'AudioBook' }), true);
    assert.equal(H.itemIsSonosCompatible('track-id-only'), true);
    assert.equal(H.itemIsSonosCompatible(null), true);
});

test('play options are incompatible when any item cannot stream on Sonos', () => {
    assert.equal(H.playOptionsAreSonosCompatible({
        items: [{ Type: 'Audio', MediaType: 'Audio' }]
    }), true);
    assert.equal(H.playOptionsAreSonosCompatible({
        items: [{ Type: 'MusicAlbum' }]
    }), true);
    assert.equal(H.playOptionsAreSonosCompatible({
        items: [{ Type: 'Episode', MediaType: 'Video' }]
    }), false);
    assert.equal(H.playOptionsAreSonosCompatible({
        Items: [{ Type: 'Movie', MediaType: 'Video' }]
    }), false);
    assert.equal(H.playOptionsAreSonosCompatible({
        items: [
            { Type: 'Audio', MediaType: 'Audio' },
            { Type: 'Episode', MediaType: 'Video' }
        ]
    }), false);
    assert.equal(H.playOptionsAreSonosCompatible({ ids: ['a', 'b'] }), true);
    assert.equal(H.playOptionsAreSonosCompatible(null), true);
});

test('isSonosBoundPlayer is only true for a Sonos remote session', () => {
    assert.equal(H.isSonosBoundPlayer({ isLocalPlayer: true }), false);
    assert.equal(H.isSonosBoundPlayer({ isLocalPlayer: false, appName: 'Jellyfin Android' }), false);
    assert.equal(H.isSonosBoundPlayer({ isLocalPlayer: false, appName: 'Sonos' }), true);
    assert.equal(H.isSonosBoundPlayer({ isLocalPlayer: false }, 'RINCON_A'), true);
    assert.equal(H.isSonosBoundPlayer({ isLocalPlayer: false, appName: 'Jellyfin Android' }, null), false);
    assert.equal(H.isSonosBoundPlayer({
        isLocalPlayer: false,
        name: 'Remote Control',
        playableMediaTypes: ['Audio']
    }), true);
    assert.equal(H.isSonosBoundPlayer({
        isLocalPlayer: false,
        name: 'Remote Control',
        playableMediaTypes: ['Audio', 'Video']
    }), false);
});

test('ids-only play options need a library lookup before they can be treated as compatible', () => {
    assert.equal(H.playOptionsNeedItemLookup({ ids: ['episode-1'] }), true);
    assert.equal(H.playOptionsNeedItemLookup({ ItemIds: 'episode-1,episode-2' }), true);
    assert.equal(H.playOptionsNeedItemLookup({
        items: [{ Type: 'Episode', MediaType: 'Video' }]
    }), false);
    assert.equal(H.playOptionsNeedItemLookup({
        items: [{ Type: 'Audio', MediaType: 'Audio' }]
    }), false);
    assert.equal(H.playOptionsNeedItemLookup(null), false);
    assert.deepEqual(H.idsFromPlayOptions({ ids: ['a', 'b'] }), ['a', 'b']);
    assert.deepEqual(H.idsFromPlayOptions({ ItemIds: 'a,b' }), ['a', 'b']);
});

test('installPlayGuard sends audio to the original player and incompatible media to onIncompatible', async () => {
    const calls = [];
    const pm = {
        play(options) { calls.push(['play', options]); return 'played'; },
        queue(options) { calls.push(['queue', options]); return 'queued'; },
        queueNext(options) { calls.push(['queueNext', options]); return 'queued-next'; }
    };
    const ctx = {
        skip() { return false; },
        playerInfo() { return { isLocalPlayer: false, appName: 'Sonos' }; },
        isSonosBound() { return true; },
        onIncompatible(options, originalPlay, name) {
            calls.push(['incompatible', name, options]);
            return 'prompted';
        }
    };

    assert.equal(H.installPlayGuard(pm, ctx), true);
    assert.equal(H.installPlayGuard(pm, ctx), false);

    assert.equal(pm.play({ items: [{ Type: 'Audio', MediaType: 'Audio' }] }), 'played');
    assert.equal(await pm.play({ items: [{ Type: 'Episode', MediaType: 'Video' }] }), 'prompted');
    assert.equal(await pm.queue({ items: [{ Type: 'Movie' }] }), 'prompted');
    assert.equal(await pm.queueNext({ items: [{ Type: 'Series' }] }), 'prompted');

    assert.deepEqual(calls, [
        ['play', { items: [{ Type: 'Audio', MediaType: 'Audio' }] }],
        ['incompatible', 'play', { items: [{ Type: 'Episode', MediaType: 'Video' }] }],
        ['incompatible', 'queue', { items: [{ Type: 'Movie' }] }],
        ['incompatible', 'queueNext', { items: [{ Type: 'Series' }] }]
    ]);
});

test('installPlayGuard does not intercept local playback, other remotes, or busy transfers', async () => {
    const calls = [];
    const pm = {
        play(options) { calls.push(options); return 'played'; }
    };
    let skip = false;
    let info = { isLocalPlayer: true };
    let sonosBound = false;
    const ctx = {
        skip() { return skip; },
        playerInfo() { return info; },
        isSonosBound() { return sonosBound; },
        onIncompatible() { throw new Error('should not prompt'); }
    };

    H.installPlayGuard(pm, ctx);

    const video = { items: [{ Type: 'Episode', MediaType: 'Video' }] };
    assert.equal(pm.play(video), 'played');

    info = { isLocalPlayer: false, appName: 'Jellyfin Android' };
    sonosBound = false;
    assert.equal(pm.play(video), 'played');

    info = { isLocalPlayer: false, appName: 'Sonos' };
    sonosBound = true;
    skip = true;
    assert.equal(pm.play(video), 'played');

    assert.equal(calls.length, 3);
});

test('installPlayGuard looks up ids-only plays before sending them to Sonos', async () => {
    const calls = [];
    const pm = {
        play(options) { calls.push(['play', options]); return 'played'; }
    };
    const library = {
        'ep-1': { Id: 'ep-1', Type: 'Episode', MediaType: 'Video' },
        'tr-1': { Id: 'tr-1', Type: 'Audio', MediaType: 'Audio' }
    };
    const ctx = {
        skip() { return false; },
        playerInfo() { return { isLocalPlayer: false, playableMediaTypes: ['Audio'] }; },
        isSonosBound() { return true; },
        resolveItems(options) {
            const id = (options.ids || [])[options.startIndex || 0];
            return Promise.resolve(library[id] ? [library[id]] : []);
        },
        onIncompatible(options, originalPlay, name) {
            calls.push(['incompatible', name, options.ids]);
            return 'prompted';
        }
    };

    H.installPlayGuard(pm, ctx);

    assert.equal(await pm.play({ ids: ['tr-1'] }), 'played');
    assert.equal(await pm.play({ ids: ['ep-1'] }), 'prompted');
    assert.deepEqual(calls, [
        ['play', { ids: ['tr-1'] }],
        ['incompatible', 'play', ['ep-1']]
    ]);
});

test('volume helpers read the Sonos queue and clamp 0-100', () => {
    assert.equal(H.volumeFromQueue({ volume: 18 }), 18);
    assert.equal(H.volumeFromQueue({ Volume: '80' }), 80);
    assert.equal(H.clampVolume(-4), 0);
    assert.equal(H.clampVolume(140), 100);
    assert.equal(H.clampVolume('x'), null);
    assert.equal(H.mutedFromQueue({ muted: true }), true);
    assert.equal(H.mutedFromQueue({ Muted: false }), false);
    assert.deepEqual(H.setVolumeBody('RINCON_A', 42), {
        targetId: 'RINCON_A',
        command: 'SetVolume',
        volume: 42
    });
    assert.deepEqual(H.muteBody('RINCON_A', true), { targetId: 'RINCON_A', command: 'Unmute' });
    assert.deepEqual(H.muteBody('RINCON_A', false), { targetId: 'RINCON_A', command: 'Mute' });
});

test('Sonos Play To target id is the Jellyfin session id, not the RINCON', () => {
    const target = H.remoteTargetFromSession({
        Id: 'jf-session',
        DeviceId: 'RINCON_TESTPLAYER1',
        DeviceName: 'Room A',
        Client: 'Sonos'
    });

    assert.equal(target.id, 'jf-session');
    assert.equal(target.playerName, 'Remote Control');
    assert.equal(target.deviceName, 'Room A');
    assert.equal(target.appName, 'Sonos');
    assert.equal(target.isLocalPlayer, false);
    assert.ok(Array.isArray(target.supportedCommands));
    assert.ok(target.supportedCommands.indexOf('SetVolume') !== -1);
    assert.ok(target.supportedCommands.indexOf('SetRepeatMode') !== -1);
});

test('remoteTargetFromSession copies session capability commands when present', () => {
    const target = H.remoteTargetFromSession({
        Id: 'jf-session',
        DeviceName: 'Room A',
        Capabilities: { SupportedCommands: ['SetVolume', 'Mute'] }
    });
    assert.deepEqual(target.supportedCommands, ['SetVolume', 'Mute']);
});

test('captured queue becomes local playbackManager.play options (ids + index + ticks)', () => {
    const opts = H.playOptionsFromCaptured({
        items: [{ Id: 'track-a' }, { id: 'track-b' }],
        index: 1,
        ticks: 42
    }, 'server-1');

    assert.deepEqual(opts.ids, ['track-a', 'track-b']);
    assert.equal(opts.startIndex, 1);
    assert.equal(opts.startPositionTicks, 42);
    assert.equal(opts.serverId, 'server-1');
    assert.equal(opts.items, undefined);
});

test('sonosPlayBodyFromCaptured keeps startIndex and startPositionTicks', () => {
    const body = H.sonosPlayBodyFromCaptured({
        items: [{ ItemId: 'track-a' }, { id: 'track-b' }],
        index: 1,
        ticks: 9000000
    }, 'RINCON_B');

    assert.equal(body.targetId, 'RINCON_B');
    assert.deepEqual(body.itemIds, ['track-a', 'track-b']);
    assert.equal(body.startIndex, 1);
    assert.equal(body.startPositionTicks, 9000000);
});

test('snapshotFromQueue uses GET /Sonos/Queue currentIndex and positionTicks', () => {
    const snap = H.snapshotFromQueue({
        items: [{ ItemId: 'a' }, { ItemId: 'b' }, { ItemId: 'c' }],
        currentIndex: 2,
        positionTicks: 1234,
        state: 'Playing'
    });

    assert.deepEqual(snap.ids, ['a', 'b', 'c']);
    assert.equal(snap.index, 2);
    assert.equal(snap.ticks, 1234);
    assert.equal(snap.itemId, 'c');
});

test('snapshotFromLocal prefers NowPlayingItem id over getCurrentPlaylistIndex', () => {
    const snap = H.snapshotFromLocal(
        [{ Id: 'a' }, { Id: 'b' }, { Id: 'c' }],
        {
            NowPlayingItem: { Id: 'c' },
            PlayState: { PositionTicks: 77 }
        },
        0
    );

    assert.equal(snap.index, 2);
    assert.equal(snap.ticks, 77);
    assert.equal(snap.itemId, 'c');
});

test('sameQueue requires identical ids and the same current item', () => {
    const captured = { ids: ['a', 'b'], index: 1, itemId: 'b', ticks: 10 };
    assert.equal(H.sameQueue(captured, {
        items: [{ Id: 'a' }, { Id: 'b' }],
        currentIndex: 1
    }), true);
    assert.equal(H.sameQueue(captured, {
        items: [{ Id: 'a' }, { Id: 'b' }],
        currentIndex: 0
    }), false);
    assert.equal(H.sameQueue(captured, {
        items: [{ Id: 'a' }, { Id: 'c' }],
        currentIndex: 1
    }), false);
});

test('speaker to local: stop Sonos and wait idle before bindLocal then playLocal', () => {
    const steps = H.plan({
        destination: 'local',
        currentlyRemote: true,
        currentCoordinatorId: 'RINCON_A',
        hasQueue: true,
        startIndex: 1,
        ticks: 42
    });

    assert.deepEqual(steps.map((s) => s.type), [
        'stopSonos',
        'waitSonosIdle',
        'bindLocal',
        'playLocal'
    ]);
    assert.equal(steps[0].coordinatorId, 'RINCON_A');
    assert.ok(!steps.some((s) => s.type === 'bindSonos' || s.type === 'playSonosApi' || s.type === 'playCaptured'));
});

test('speaker to local without a captured queue only stops Sonos and binds the bar', () => {
    const steps = H.plan({
        destination: 'local',
        currentlyRemote: true,
        currentCoordinatorId: 'RINCON_A',
        hasQueue: false
    });

    assert.deepEqual(steps.map((s) => s.type), [
        'stopSonos',
        'waitSonosIdle',
        'bindLocal'
    ]);
    assert.ok(!steps.some((s) => s.type === 'playLocal'));
});

test('local to speaker: stop local, play via Sonos API, then bind the bar last', () => {
    const steps = H.plan({
        destination: 'sonos',
        coordinatorId: 'RINCON_B',
        sessionId: 'sess-b',
        currentlyRemote: false,
        hasQueue: true,
        startIndex: 1,
        ticks: 42
    });

    assert.deepEqual(steps.map((s) => s.type), [
        'stopLocal',
        'waitLocalIdle',
        'playSonosApi',
        'waitSonosPlaying',
        'bindSonos',
        'revealBar'
    ]);
    assert.equal(steps[2].coordinatorId, 'RINCON_B');
    assert.equal(steps[2].startIndex, 1);
    assert.equal(steps[2].ticks, 42);
    assert.equal(steps[4].sessionId, 'sess-b');
    assert.ok(!steps.some((s) => s.type === 'playLocal' || s.type === 'playCaptured'));
});

test('same queue on the destination seeks instead of Queue/Play', () => {
    const steps = H.plan({
        destination: 'sonos',
        coordinatorId: 'RINCON_B',
        sessionId: 'sess-b',
        currentlyRemote: false,
        hasQueue: true,
        sameQueue: true,
        startIndex: 1,
        ticks: 99
    });

    assert.deepEqual(steps.map((s) => s.type), [
        'stopLocal',
        'waitLocalIdle',
        'seekSonos',
        'waitSonosPlaying',
        'bindSonos',
        'revealBar'
    ]);
    assert.equal(steps[2].positionTicks, 99);
    assert.ok(!steps.some((s) => s.type === 'playSonosApi'));
});

test('speaker to another speaker stops the old coordinator before playing the new one', () => {
    const steps = H.plan({
        destination: 'sonos',
        coordinatorId: 'RINCON_B',
        sessionId: 'sess-b',
        currentlyRemote: true,
        currentCoordinatorId: 'RINCON_A',
        hasQueue: true,
        startIndex: 0,
        ticks: 5
    });

    assert.deepEqual(steps.map((s) => s.type), [
        'stopSonos',
        'waitSonosIdle',
        'playSonosApi',
        'waitSonosPlaying',
        'bindSonos',
        'revealBar'
    ]);
    assert.equal(steps[0].coordinatorId, 'RINCON_A');
    assert.equal(steps[2].coordinatorId, 'RINCON_B');
});

test('local idle to speaker with no queue only binds Remote Control', () => {
    const steps = H.plan({
        destination: 'sonos',
        coordinatorId: 'RINCON_B',
        sessionId: 'sess-b',
        currentlyRemote: false,
        hasQueue: false
    });

    assert.deepEqual(steps.map((s) => s.type), [
        'stopLocal',
        'waitLocalIdle',
        'bindSonos',
        'revealBar'
    ]);
    assert.equal(steps[2].coordinatorId, 'RINCON_B');
    assert.equal(steps[2].sessionId, 'sess-b');
    assert.ok(!steps.some((s) => s.type === 'playSonosApi' || s.type === 'waitSonosPlaying' || s.type === 'seekSonos'));
});

test('speaker to another speaker with no queue stops the old coordinator then binds', () => {
    const steps = H.plan({
        destination: 'sonos',
        coordinatorId: 'RINCON_B',
        sessionId: 'sess-b',
        currentlyRemote: true,
        currentCoordinatorId: 'RINCON_A',
        hasQueue: false
    });

    assert.deepEqual(steps.map((s) => s.type), [
        'stopSonos',
        'waitSonosIdle',
        'bindSonos',
        'revealBar'
    ]);
    assert.equal(steps[0].coordinatorId, 'RINCON_A');
    assert.equal(steps[2].coordinatorId, 'RINCON_B');
    assert.ok(!steps.some((s) => s.type === 'playSonosApi' || s.type === 'waitSonosPlaying'));
});

test('execute local-to-sonos never starts Sonos until local is idle', async () => {
    let localPlaying = true;
    const order = [];

    await H.execute(H.plan({
        destination: 'sonos',
        coordinatorId: 'RINCON_B',
        sessionId: 'sess-b',
        currentlyRemote: false,
        hasQueue: true,
        startIndex: 1,
        ticks: 42
    }), {
        stopLocal() {
            order.push('stopLocal');
            localPlaying = false;
        },
        waitLocalIdle() {
            order.push('waitLocalIdle');
            assert.equal(localPlaying, false);
        },
        playSonosApi(step) {
            order.push('playSonosApi');
            assert.equal(localPlaying, false);
            assert.equal(step.ticks, 42);
            assert.equal(step.startIndex, 1);
        },
        waitSonosPlaying() {
            order.push('waitSonosPlaying');
        },
        bindSonos(step) {
            order.push('bindSonos:' + step.sessionId);
        },
        revealBar() {
            order.push('revealBar');
        },
        playLocal() {
            throw new Error('pm.play must not start Sonos');
        }
    });

    assert.deepEqual(order, [
        'stopLocal',
        'waitLocalIdle',
        'playSonosApi',
        'waitSonosPlaying',
        'bindSonos:sess-b',
        'revealBar'
    ]);
});

test('execute sonos-to-local plays only after Remote Control is unbound', async () => {
    const pm = {
        info: { isLocalPlayer: false, id: 'sess-a' },
        getPlayerInfo() { return this.info; }
    };
    const order = [];

    await H.execute(H.plan({
        destination: 'local',
        currentlyRemote: true,
        currentCoordinatorId: 'RINCON_A',
        hasQueue: true
    }), {
        stopSonos(step) {
            order.push('stopSonos:' + step.coordinatorId);
        },
        waitSonosIdle() {
            order.push('waitSonosIdle');
        },
        bindLocal() {
            order.push('bindLocal');
            assert.equal(H.playTarget(pm.getPlayerInfo()), 'remote', 'must still be remote before bind');
            pm.info = { isLocalPlayer: true };
            return H.bindLocal({
                setDefaultPlayerActive() { order.push('setDefaultPlayerActive'); },
                setActivePlayer() { throw new Error('trySetActivePlayer-style local bind must not be used'); },
                getPlayerInfo() { return pm.info; },
                play() {},
                trySetActivePlayer() { throw new Error('trySetActivePlayer(localplayer) is a no-op on 10.11'); }
            });
        },
        playLocal() {
            order.push('playLocal');
            assert.equal(H.playTarget(pm.getPlayerInfo()), 'local');
        }
    });

    assert.deepEqual(order, [
        'stopSonos:RINCON_A',
        'waitSonosIdle',
        'bindLocal',
        'setDefaultPlayerActive',
        'playLocal'
    ]);
});

test('haltLocalPlayback pauses then uses playbackManager.stop so html5 does not auto-advance', async () => {
    const order = [];
    const player = {
        isLocalPlayer: true,
        pause() { order.push('pause'); },
        stop() { order.push('player.stop'); }
    };
    const pm = {
        getCurrentPlayer() { return player; },
        stop(p) {
            order.push('playbackManager.stop');
            assert.equal(p, player);
            return Promise.resolve();
        }
    };

    await H.haltLocalPlayback(pm);
    assert.deepEqual(order, ['pause', 'playbackManager.stop']);
});

test('haltLocalPlayback never calls player.stop when playbackManager.stop is missing', async () => {
    const order = [];
    const player = {
        isLocalPlayer: true,
        pause() { order.push('pause'); },
        stop() { order.push('player.stop'); }
    };
    await H.haltLocalPlayback({
        getCurrentPlayer() { return player; }
    });
    assert.deepEqual(order, ['pause']);
});

test('localIsIdle treats a paused html5 player as idle even with a currentSrc', () => {
    assert.equal(H.localIsIdle(null), true);
    assert.equal(H.localIsIdle({
        getPlayerInfo() { return { isLocalPlayer: false }; },
        getCurrentPlayer() { return { isLocalPlayer: false }; }
    }), true);
    assert.equal(H.localIsIdle({
        getPlayerInfo() { return { isLocalPlayer: true }; },
        getCurrentPlayer() { return { isLocalPlayer: true, currentSrc() { return 'http://x'; } }; },
        isPlaying() { return true; }
    }), false);
    assert.equal(H.localIsIdle({
        getPlayerInfo() { return { isLocalPlayer: true }; },
        getCurrentPlayer() { return { isLocalPlayer: true, currentSrc() { return ''; } }; },
        isPlaying() { return false; }
    }), true);
    assert.equal(H.localIsIdle({
        getPlayerInfo() { return { isLocalPlayer: true }; },
        getCurrentPlayer() {
            return {
                isLocalPlayer: true,
                paused() { return true; },
                currentSrc() { return 'http://local/track'; }
            };
        }
    }), true);
});

test('findPlaybackManager loads the factory that owns trySetActivePlayer', () => {
    const pm = {
        getPlayerInfo() { return { isLocalPlayer: true }; },
        setActivePlayer() {},
        setDefaultPlayerActive() {},
        trySetActivePlayer() {},
        play() {}
    };
    function otherFactory() { /* no player apis */ }
    function playbackFactory() {
        return 'trySetActivePlayer setDefaultPlayerActive';
    }

    const req = function (id) {
        if (id === 'pm') {
            return { playbackManager: pm };
        }
        return {};
    };
    req.m = { other: otherFactory, pm: playbackFactory };

    assert.equal(H.findPlaybackManager(req), pm);
});

test('nowPlayingLooksReady requires a named NowPlayingItem', () => {
    assert.equal(H.nowPlayingLooksReady(null), false);
    assert.equal(H.nowPlayingLooksReady({}), false);
    assert.equal(H.nowPlayingLooksReady({ NowPlayingItem: {} }), false);
    assert.equal(H.nowPlayingLooksReady({ NowPlayingItem: { Name: 'Track Three' } }), true);
    assert.equal(H.nowPlayingLooksReady({ nowPlayingItem: { name: 'Track Four' } }), true);
});

test('triggerPlayerEvent matches jellyfin-web Events (fn.apply(obj, [{ type }, ...args]))', () => {
    const seen = [];
    const player = {
        _callbacks: {
            playbackstart: [function (e, state) {
                seen.push({ thisArg: this, type: e.type, item: state.NowPlayingItem.Name });
            }]
        }
    };

    assert.equal(H.triggerPlayerEvent(player, 'playbackstart', [{ NowPlayingItem: { Name: 'Track One' } }]), true);
    assert.equal(seen.length, 1);
    assert.equal(seen[0].thisArg, player);
    assert.equal(seen[0].type, 'playbackstart');
    assert.equal(seen[0].item, 'Track One');
});

test('revealNowPlayingBar uses captured fallback and never fires init', () => {
    const types = [];
    const player = {
        isLocalPlayer: false,
        _callbacks: {
            playbackstart: [function (e) { types.push(e.type); }]
        }
    };
    const pm = {
        getCurrentPlayer() { return player; },
        getPlayerState() { return {}; }
    };

    assert.equal(H.revealNowPlayingBar(pm, {
        itemId: 'track-1',
        index: 0,
        items: [{ Id: 'track-1', Name: 'Track Three' }]
    }), true);
    assert.deepEqual(types, ['playbackstart']);
    assert.ok(!types.includes('init'));
});

test('applyNowPlayingAliases copies camelCase session fields the bar actually reads', () => {
    const item = H.applyNowPlayingAliases({
        name: 'Track One',
        album: 'Example Album',
        artists: ['Example Artist'],
        serverId: 'srv',
        imageTags: { primary: 'tag' },
        albumId: 'album-1',
        albumPrimaryImageTag: 'album-tag',
        runTimeTicks: 9
    });
    assert.equal(item.Name, 'Track One');
    assert.equal(item.Album, 'Example Album');
    assert.deepEqual(item.Artists, ['Example Artist']);
    assert.equal(item.ServerId, 'srv');
    assert.equal(item.ImageTags.Primary, 'tag');
    assert.equal(item.AlbumId, 'album-1');
    assert.equal(item.AlbumPrimaryImageTag, 'album-tag');
    assert.equal(item.RunTimeTicks, 9);
});

test('installNowPlayingPatch mutates a camelCase Sessions payload before later listeners run', () => {
    let seenName;
    const player = {
        isLocalPlayer: false,
        _callbacks: {
            statechange: [function (e, state) {
                seenName = state.NowPlayingItem && state.NowPlayingItem.Name;
            }]
        }
    };
    const captured = {
        items: [{ Id: 'track-1', Name: 'Track Three', Artists: ['Example Artist'] }]
    };
    assert.equal(H.installNowPlayingPatch(player, captured, 'srv'), true);

    const session = { nowPlayingItem: { id: 'track-1', runTimeTicks: 42 } };
    player._callbacks.statechange.slice().forEach((fn) => fn.apply(player, [{ type: 'statechange' }, session]));
    assert.equal(seenName, 'Track Three');
    assert.equal(session.NowPlayingItem.Name, 'Track Three');
    assert.equal(session.NowPlayingItem.ServerId, 'srv');
});

test('mergeNowPlayingItem ignores an unnamed session stub and keeps captured title', () => {
    const item = H.mergeNowPlayingItem(
        { Id: 'stub', MediaType: 'Audio' },
        {
            itemId: 'track-1',
            index: 0,
            items: [{
                Id: 'track-1',
                Name: 'Track Three',
                Album: 'Example Album',
                Artists: ['Example Artist']
            }]
        },
        'server-1'
    );
    assert.equal(item.Name, 'Track Three');
    assert.equal(item.Album, 'Example Album');
    assert.deepEqual(item.Artists, ['Example Artist']);
    assert.equal(item.ServerId, 'server-1');
    assert.equal(item.MediaType, 'Audio');
});

test('mergeNowPlayingItem keeps session artwork tags', () => {
    const item = H.mergeNowPlayingItem(
        {
            Name: 'Track Four',
            ImageTags: { Primary: 'tag' },
            AlbumId: 'album-1',
            AlbumPrimaryImageTag: 'album-tag',
            ServerId: 'from-session'
        },
        { items: [{ Id: 't', Name: 'Track Four' }] },
        'fallback-server'
    );
    assert.equal(item.ImageTags.Primary, 'tag');
    assert.equal(item.AlbumId, 'album-1');
    assert.equal(item.AlbumPrimaryImageTag, 'album-tag');
    assert.equal(item.ServerId, 'from-session');
});

test('revealNowPlayingBar does not paint an unnamed session stub', () => {
    let painted;
    const player = {
        isLocalPlayer: false,
        _callbacks: {
            playbackstart: [function (e, state) { painted = state.NowPlayingItem; }]
        }
    };
    assert.equal(H.revealNowPlayingBar({
        getCurrentPlayer() { return player; },
        getPlayerState() { return { NowPlayingItem: { Id: 'stub' } }; }
    }, {
        items: [{ Id: 'track-1', Name: 'Track One', Artists: ['Example Artist'] }]
    }, 'srv'), true);
    assert.equal(painted.Name, 'Track One');
    assert.deepEqual(painted.Artists, ['Example Artist']);
    assert.equal(painted.ServerId, 'srv');
});

test('revealNowPlayingBar is a no-op on the local html5 player', () => {
    const player = {
        isLocalPlayer: true,
        _callbacks: { playbackstart: [function () { throw new Error('must not fire'); }] }
    };
    assert.equal(H.revealNowPlayingBar({
        getCurrentPlayer() { return player; },
        getPlayerState() { return { NowPlayingItem: { Name: 'x' } }; }
    }, {}), false);
});

test('isPlaybackManager requires setActivePlayer because trySetActivePlayer cannot go local', () => {
    assert.equal(H.isPlaybackManager({
        getPlayerInfo() {},
        trySetActivePlayer() {},
        play() {}
    }), false);
    assert.equal(H.isPlaybackManager({
        getPlayerInfo() {},
        setActivePlayer() {},
        trySetActivePlayer() {},
        play() {}
    }), true);
});

function ownedQueue(overrides) {
    return Object.assign({
        coordinatorId: 'RINCON_A',
        userId: '11111111-1111-1111-1111-111111111111',
        pluginOwned: true,
        state: 'Playing',
        items: [{ Id: 'track-1', Name: 'Track One' }]
    }, overrides);
}

test('queueOwnedByUser requires this Jellyfin user, items, active state, and pluginOwned', () => {
    const me = '11111111111111111111111111111111';
    assert.equal(H.queueOwnedByUser(ownedQueue(), me), true);
    assert.equal(H.queueOwnedByUser(ownedQueue({ UserId: me, userId: undefined }), me), true);
    assert.equal(H.queueOwnedByUser(ownedQueue({ userId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' }), me), false);
    assert.equal(H.queueOwnedByUser(ownedQueue({ userId: '' }), me), false);
    assert.equal(H.queueOwnedByUser(ownedQueue({ userId: '00000000-0000-0000-0000-000000000000' }), me), false);
    assert.equal(H.queueOwnedByUser(ownedQueue(), ''), false);
    assert.equal(H.queueOwnedByUser(ownedQueue({ pluginOwned: false }), me), false);
    assert.equal(H.queueOwnedByUser(ownedQueue({ items: [] }), me), false);
    assert.equal(H.queueOwnedByUser(ownedQueue({ state: 'Stopped' }), me), false);
    assert.equal(H.queueOwnedByUser(ownedQueue({ state: 'Paused' }), me), true);
    assert.equal(H.queueOwnedByUser(ownedQueue({ state: 'Transitioning' }), me), true);
});

test('pickRestoreTarget prefers Playing over Paused for this user', () => {
    const me = '11111111-1111-1111-1111-111111111111';
    const other = ownedQueue({
        coordinatorId: 'RINCON_B',
        userId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        state: 'Playing'
    });
    const pausedMine = ownedQueue({ coordinatorId: 'RINCON_A', state: 'Paused' });
    const playingMine = ownedQueue({ coordinatorId: 'RINCON_C', state: 'Playing' });
    const notOwned = ownedQueue({ coordinatorId: 'RINCON_D', pluginOwned: false, state: 'Playing' });

    assert.equal(H.pickRestoreTarget([other, pausedMine, playingMine, notOwned], me).coordinatorId, 'RINCON_C');
    assert.equal(H.pickRestoreTarget([other, pausedMine, notOwned], me).coordinatorId, 'RINCON_A');
    assert.equal(H.pickRestoreTarget([other, notOwned], me), null);
});

test('restorePlan binds then reveals and never plays, stops, or seeks', () => {
    const steps = H.restorePlan({ coordinatorId: 'RINCON_B', sessionId: 'sess-b' });
    assert.deepEqual(steps.map((s) => s.type), ['bindSonos', 'revealBar']);
    assert.equal(steps[0].coordinatorId, 'RINCON_B');
    assert.equal(steps[0].sessionId, 'sess-b');
    assert.ok(!steps.some((s) => s.type === 'playSonosApi' || s.type === 'seekSonos' || s.type === 'stopSonos' || s.type === 'stopLocal' || s.type === 'playLocal'));
});
