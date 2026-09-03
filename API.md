# Sonos plugin HTTP API

Authenticated Jellyfin clients (official apps, third-party UIs, scripts) call `/Sonos` to discover speakers, group rooms, build a queue from the music library, and control playback. The injected jellyfin-web UI uses the same routes; they are not a private web-only surface.

After `Queue/Play` (and grouping while a Jellyfin queue is loaded), the plugin loads **Cloud Queue** on the coordinator. Speakers then fetch queue JSON, audio, and artwork from tokenized `/Sonos/queue`, `/Sonos/stream`, and `/Sonos/image` URLs. Jellyfin clients do not call those paths; they are documented here because they explain playback, `queueVersion`, disconnect behavior, and errors such as `PublishedUrlInvalid` and `ERROR_CLOUD_QUEUE_SERVICE_ERROR`.

Operational setup (seed IPs, published base URL, LAN auth) lives in [README.md](README.md).

## Base URL

All paths are relative to the Jellyfin server origin, including any custom base URL:

```
{origin}{baseUrl}/Sonos/...
```

Examples:

```
http://192.0.2.10:8096/Sonos/Players
http://192.0.2.10:8096/media/Sonos/Players
```

JSON request and response bodies use **camelCase** property names (`targetId`, `itemIds`, `coordinatorId`). Clients should also accept PascalCase if a proxy rewrites JSON. Null properties may be omitted.

Time positions on `/Sonos/Queue` and `/Sonos/Playstate` use **Jellyfin ticks** (10,000,000 ticks = 1 second). Cloud Queue payloads use milliseconds.

`Content-Type` for JSON request bodies: `application/json`.

## Authentication

Player, group, queue, and playstate routes are `[Authorize]`. Send a Jellyfin API key or session token the same way other Jellyfin APIs expect it:

```http
Authorization: MediaBrowser Token=${JELLYFIN_API_KEY}
```

`X-Emby-Token` and the usual `ApiClient` headers also work. Unauthenticated calls return Jellyfin’s standard **401** (not a Sonos problem body).

Example:

```bash
curl -sS -H "Authorization: MediaBrowser Token=${JELLYFIN_API_KEY}" \
  http://127.0.0.1:8096/Sonos/Players
```

`POST /Sonos/Queue/Play` and `POST /Sonos/Queue/Add` resolve the Jellyfin user from the token (`NameIdentifier` / `UserId` / `Jellyfin-UserId`). If the token has no user, the plugin **Default user** setting is used. If neither is set, the API returns `UserRequired`.

Library visibility follows that user: items the user cannot see are `ItemNotFound`.

Cloud Queue, stream, and image URLs are loaded onto the speaker by the plugin. They use HMAC path tokens, not Jellyfin cookies. Do not log tokens. They expire **24 hours** after mint (play/add).

## Identifiers

| Kind | Example | Notes |
| --- | --- | --- |
| Player id | `RINCON_XXXXXXXXXXXXXXXXX` | Sonos UUID. Case-insensitive. |
| Group id | `RINCON_XXXXXXXXXXXXXXXXX:1` | Coordinator’s current Sonos group id. |
| Target id | either of the above | Queue and playstate resolve a player **or** group to the **coordinator**. Passing a satellite RINCON targets that room’s coordinator. |
| Jellyfin item id | GUID | Must be an **Audio** item. Albums, playlists, and artists must be expanded to track ids by the client. |
| Queue item id | 32-char hex GUID (`N`) | Immutable id of one slot in the plugin logical queue. Used by Cloud Queue and `Queue/Remove`. Distinct from the Jellyfin `itemId`. |

Bonded satellites (stereo pair, surrounds) follow their coordinator. Pass coordinator RINCONs in grouping requests, not every satellite.

Ignored player ids (config) never appear in `GET /Sonos/Players` and cannot be targeted.

## Errors

JSON errors use a problem body. No stack traces.

```json
{
  "error": "PlayerUnavailable",
  "message": "Kitchen did not respond to loadCloudQueue",
  "details": {
    "httpStatus": 403,
    "player": "Kitchen"
  }
}
```

| Field | Type | Description |
| --- | --- | --- |
| `error` | string | Stable machine code. Match on this, not `message`. |
| `message` | string | Human-readable. Safe to show. |
| `details` | object, optional | Extra fields (`httpStatus` from the speaker, `player` room name). Omitted when empty. |

### Error codes

| `error` | Typical HTTP | When |
| --- | --- | --- |
| `InvalidTarget` | 400 | `targetId` missing or blank. |
| `InvalidRequest` | 400 | Missing `itemIds`, fewer than two `playerIds`, empty member change, or move indexes out of range. |
| `UnknownCommand` | 400 | `POST /Sonos/Playstate` `command` is not in the known set. |
| `UserRequired` | 400 | Play/Add cannot resolve a Jellyfin user (no token user and no Default user). |
| `NotAudio` | 400 | An `itemIds` entry is not an audio item (for example an album or video id). |
| `PublishedUrlInvalid` | 400 | Published base URL missing, not `http(s)`, loopback, link-local (`169.254/16`), or Docker-bridge (`172.16.0.0/12`). Speakers could not fetch audio. |
| `PluginDisabled` | 403 | Plugin **Enabled** is off. Discovery snapshot still works; mutating routes do not. |
| `LanAuthRequired` | 403 | Speaker returned HTTP 403. Allow third-party LAN control in the Sonos app. |
| `InvalidToken` | 403 | Stream/image path token is missing, malformed, or has a bad HMAC. |
| `PlayerNotFound` | 404 | No discovered player or group matched the id. |
| `QueueNotFound` | 404 | Remove/Move against a coordinator that has never had a plugin queue. |
| `ItemNotFound` | 404 | Library item missing, not visible to the user, or audio file gone (stream). |
| `StreamExpired` | 410 | Stream/image token past expiry. Play the queue again to mint new tokens. |
| `PlayerUnavailable` | 409 | Coordinator offline, LAN websocket failed, grouping timeout, or speaker did not respond. |
| `NotSupported` | 409 | Control path cannot perform the command (mapped from SOAP-only fallback). Create-group may still succeed via SOAP `x-rincon`. |
| `CommandFailed` | 409 | LAN Control command failed. |
| `ERROR_CLOUD_QUEUE_SERVICE_ERROR` | 502 | Speaker reported a Cloud Queue failure (speaker could not fetch the plugin queue URL). |

Speaker control failures include `details.player` (room name) and `details.httpStatus` when the speaker HTTP status is known.

Jellyfin framework **401** (no/invalid session) and ASP.NET **400** (malformed JSON) do not use this body.

---

## Routes

Mutating routes return **403** `PluginDisabled` when the plugin is disabled.

Successful queue mutations return the same snapshot as `GET /Sonos/Queue`. Successful grouping mutations return the same shape as `GET /Sonos/Groups`.

### `GET /Sonos/Players`

Discovered S2 players plus current groups. Cheap; no speaker round-trip. Volume/mute on each player are last-known values (often absent until a queue poll or `GET /Players/{id}`).

**200** `PlayersResponse`

```json
{
  "players": [
    {
      "id": "RINCON_TESTPLAYER1",
      "name": "Kitchen",
      "model": "S6",
      "modelDisplayName": "Play:5",
      "ip": "192.0.2.21",
      "groupId": "RINCON_TESTPLAYER1:1",
      "isCoordinator": true,
      "available": true,
      "volume": 18,
      "muted": false,
      "capabilities": {
        "gapless": true,
        "crossfade": true,
        "maxSampleRate": 48000,
        "maxBitDepth": 16,
        "nativeCodecs": ["flac", "mp3", "aac"]
      }
    }
  ],
  "groups": [
    {
      "id": "RINCON_TESTPLAYER1:1",
      "name": "Kitchen + Lounge",
      "coordinatorId": "RINCON_TESTPLAYER1",
      "memberIds": ["RINCON_TESTPLAYER1", "RINCON_TESTPLAYER2"],
      "playbackState": "Stopped"
    }
  ]
}
```

S1 speakers, invisible satellites, and ignored ids are omitted. Players unseen for three discovery cycles have `available: false`.

`groups[].playbackState` on this snapshot is not a live transport poll (currently `Stopped`). Use `GET /Sonos/Queue` for playback state.

`capabilities` is the S2 default profile used by the transcode planner.

Empty registry:

```json
{ "players": [], "groups": [] }
```

### `GET /Sonos/Players/{id}`

One player. `{id}` is a player or group id. Live volume and mute are fetched from the speaker when cheap; if that fails, last-known values are returned.

**200** `PlayerInfo` (same object as one element of `players` above).

| Status | `error` |
| --- | --- |
| 404 | `PlayerNotFound` |

### Grouping

`GET /Sonos/Groups` lists current groups. `POST /Sonos/Groups` joins the listed players into one group. `POST /Sonos/Groups/{id}/Members` adds or removes players (`playerIdsToAdd` / `playerIdsToRemove`).

Bonded satellites (stereo pair, surrounds) follow their coordinator — pass coordinator RINCONs, not every satellite. Grouping uses the Sonos LAN Control API, with SOAP `x-rincon:` join as fallback. If the coordinator was playing a Jellyfin queue, Cloud Queue is reloaded on the new group at the current playhead.

### `GET /Sonos/Groups`

Current groups only (`{ "groups": [ ... ] }`), same `GroupInfo` objects as `GET /Sonos/Players`.

**200** `GroupsResponse`

### `POST /Sonos/Groups`

Joins the listed players into one group. Bonded satellites follow their coordinator.

**Body** `CreateGroupRequest`

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `playerIds` | string[] | yes | At least two distinct player ids after trim. Duplicates ignored (case-insensitive). |
| `coordinatorId` | string | no | Coordinator RINCON. Defaults to the first `playerIds` entry. |

```json
{
  "coordinatorId": "RINCON_TESTPLAYER1",
  "playerIds": [
    "RINCON_TESTPLAYER1",
    "RINCON_TESTPLAYER2"
  ]
}
```

Uses the Sonos LAN Control API, with SOAP `x-rincon:` join as fallback. If the coordinator was playing a Jellyfin queue, Cloud Queue is reloaded on the new group at the current playhead.

**200** `GroupsResponse`

| Status | `error` |
| --- | --- |
| 400 | `InvalidRequest` — fewer than two player ids |
| 403 | `PluginDisabled` |
| 404 | `PlayerNotFound` — coordinator or a listed player |
| 409 | `PlayerUnavailable`, `LanAuthRequired`, other control errors |

### `POST /Sonos/Groups/{id}/Members`

Adds or removes players on an existing group. `{id}` is a group id or any member/coordinator id that resolves to that group.

**Body** `ModifyGroupMembersRequest`

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `playerIdsToAdd` | string[] | one of add/remove | Player ids to join. |
| `playerIdsToRemove` | string[] | one of add/remove | Player ids to split off (become standalone). |

```json
{
  "playerIdsToAdd": ["RINCON_TESTPLAYER3"],
  "playerIdsToRemove": []
}
```

Same resume-after-grouping behavior as create.

**200** `GroupsResponse`

| Status | `error` |
| --- | --- |
| 400 | `InvalidRequest` — both lists empty |
| 403 | `PluginDisabled` |
| 404 | `PlayerNotFound` |
| 409 | speaker control errors |

---

### Queue snapshot (`QueueResponse`)

Returned by Play, Add, Remove, Move, Get Queue, and Playstate.

```json
{
  "coordinatorId": "RINCON_TESTPLAYER1",
  "state": "Playing",
  "repeat": "None",
  "shuffle": false,
  "crossfade": false,
  "volume": 18,
  "muted": false,
  "positionTicks": 450000000,
  "currentIndex": 0,
  "queueVersion": "1710000000000",
  "userId": "11111111-1111-1111-1111-111111111111",
  "pluginOwned": true,
  "items": [
    {
      "queueItemId": "a1b2c3d4e5f64789a0b1c2d3e4f50617",
      "itemId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      "name": "Hold On",
      "album": "Down the Way",
      "artists": ["Angus & Julia Stone"],
      "durationTicks": 2430000000,
      "directPlay": true,
      "transcodeReason": null
    }
  ]
}
```

| Field | Type | Description |
| --- | --- | --- |
| `coordinatorId` | string | Group coordinator RINCON. |
| `state` | string | `Stopped`, `Playing`, `Paused`, or `Transitioning`. Treat case-insensitively. |
| `repeat` | string | `None`, `All`, or `One`. |
| `shuffle` | bool | Shuffle on the logical queue. |
| `crossfade` | bool | Crossfade on the speaker. |
| `volume` | int | 0–100, group/coordinator. |
| `muted` | bool | Mute on the coordinator. |
| `positionTicks` | long | Playhead in the current track. |
| `currentIndex` | int | Index into `items`. |
| `queueVersion` | string | Bumped on every queue rewrite. Speakers poll this via Cloud Queue. |
| `userId` | GUID | Jellyfin user who started this queue (token mint / library access). |
| `pluginOwned` | bool | `true` after a successful load (Cloud Queue or SOAP). `false` after `Stop`. The speaker may still be playing non-Jellyfin content when `false`. |
| `items` | array | Logical queue. |

**`items[]`**

| Field | Type | Description |
| --- | --- | --- |
| `queueItemId` | string | Plugin queue slot id. |
| `itemId` | GUID | Jellyfin library id. |
| `name` | string | Track title. |
| `album` | string | Album title. |
| `artists` | string[] | Artists, else album artists. |
| `durationTicks` | long | Duration. |
| `directPlay` | bool | Original file is streamed unmodified. |
| `transcodeReason` | string or null | Omitted/`null` when direct play. Otherwise one of: `ContainerNotSupported`, `CodecNotSupported`, `SampleRateTooHigh`, `BitDepthTooHigh`, `ChannelCount`, `AlbumRateMatch`. |

Direct play: 16-bit FLAC / MP3 / AAC at ≤48 kHz, stereo. Mixed 44.1/48 albums are transcoded to one rate when gapless/crossfade needs it (`AlbumRateMatch`). Preferred transcode codec is the plugin setting (`flac` default, or `aac`).

### `POST /Sonos/Queue/Play`

Replaces the logical queue and starts playback on the target’s coordinator.

**Body** `PlayQueueRequest`

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `targetId` | string | yes | Player or group id. |
| `itemIds` | GUID[] | yes | Audio item ids, in play order. Must be non-empty. |
| `startIndex` | int | no | Index into `itemIds` (clamped). Default `0`. |
| `startPositionTicks` | long | no | Seek into the first playing track. Default `0`. |

```json
{
  "targetId": "RINCON_TESTPLAYER1",
  "itemIds": [
    "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "bbbbbbbb-cccc-dddd-eeee-ffffffffffff"
  ],
  "startIndex": 0,
  "startPositionTicks": 0
}
```

Loads native Cloud Queue when the speaker supports it. Falls back to SOAP `SetAVTransportURI` of `/Sonos/stream/{token}` when LAN Cloud Queue is unavailable (`NotSupported`, `PlayerUnavailable`, or `LanAuthRequired` on load — those codes are swallowed for the load path and SOAP is used instead; they still surface for other commands).

**200** `QueueResponse`

| Status | `error` |
| --- | --- |
| 400 | `InvalidRequest`, `InvalidTarget`, `UserRequired`, `NotAudio`, `PublishedUrlInvalid` |
| 403 | `PluginDisabled`, `LanAuthRequired` |
| 404 | `PlayerNotFound`, `ItemNotFound` |
| 409 | `PlayerUnavailable` and other control errors |
| 502 | `ERROR_CLOUD_QUEUE_SERVICE_ERROR` |

### `POST /Sonos/Queue/Add`

Appends items without replacing the queue.

**Body** `AddQueueRequest`

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `targetId` | string | yes | Player or group id. |
| `itemIds` | GUID[] | yes | Audio item ids. |
| `mode` | string | no | `Last` (default): append. `Next`: insert after the current track. Case-insensitive. |

If the coordinator already has Cloud Queue loaded, the speaker is told to refresh.

**200** `QueueResponse` — same error set as Play (minus start-index concerns).

### `POST /Sonos/Queue/Remove`

Removes slots by **queue item id**, not Jellyfin item id.

**Body** `RemoveQueueRequest`

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `targetId` | string | yes | Player or group id. |
| `queueItemIds` | string[] | no | Logical ids from `QueueResponse.items[].queueItemId`. Unknown ids are ignored. |

Current-track index is preserved when that slot remains; otherwise it is clamped.

**200** `QueueResponse`

| Status | `error` |
| --- | --- |
| 400 | `InvalidTarget` |
| 403 | `PluginDisabled` |
| 404 | `PlayerNotFound`, `QueueNotFound` |
| 409 | speaker control errors |

### `POST /Sonos/Queue/Move`

Moves one slot. Indexes are into the current `items` array.

**Body** `MoveQueueRequest`

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `targetId` | string | yes | Player or group id. |
| `fromIndex` | int | yes | Source index. |
| `toIndex` | int | yes | Destination index after removal (must be in range of the current list). |

`currentIndex` follows the moved item when it was the playing track.

**200** `QueueResponse`

| Status | `error` |
| --- | --- |
| 400 | `InvalidTarget`, `InvalidRequest` (index out of range) |
| 403 | `PluginDisabled` |
| 404 | `PlayerNotFound`, `QueueNotFound` |
| 409 | speaker control errors |

### `GET /Sonos/Queue`

Cheap poll. Transport is refreshed at most about once per second per coordinator.

```
GET /Sonos/Queue?targetId=RINCON_TESTPLAYER1
```

| Query | Type | Required |
| --- | --- | --- |
| `targetId` | string | yes |

**200** `QueueResponse`. If the coordinator has never been played through the plugin, the snapshot is empty (`items: []`, `state: Stopped`, `pluginOwned: false`).

Poll at ~1 Hz. Faster polling does not hit the speaker more often.

| Status | `error` |
| --- | --- |
| 400 | `InvalidTarget` |
| 404 | `PlayerNotFound` |
| 409 | `PlayerUnavailable` |

`GET /Sonos/Queue` does **not** return `PluginDisabled` when the plugin is off (read-only).

### `POST /Sonos/Playstate`

Transport and grouping-local commands. Always include `targetId` and `command`. Extra fields are ignored unless the command uses them.

**Body** `PlaystateRequest`

| Field | Type | Used by |
| --- | --- | --- |
| `targetId` | string | all |
| `command` | string | all — exact names below |
| `positionTicks` | long | `Seek` |
| `volume` | int | `SetVolume` (clamped 0–100) |
| `repeat` | string | `SetRepeat` — `None`, `All`, `One` (empty → `None`) |
| `shuffle` | bool | `SetShuffle` |
| `crossfade` | bool | `SetCrossfade` |

| `command` | Effect |
| --- | --- |
| `Play` | Resume. If the queue is SOAP-only (no Cloud Queue) and has items, reloads the current track. Requires a valid published base URL. |
| `Pause` | Pause. Queue stays plugin-owned. |
| `Stop` | Stop transport and set `pluginOwned: false`. |
| `Next` / `Previous` | Skip. Cloud Queue uses speaker next/previous; SOAP fallback reloads the adjacent stream URL. Requires published base URL. |
| `Seek` | Seek to `positionTicks` in the current track. |
| `SetVolume` | Coordinator volume 0–100. |
| `Mute` / `Unmute` | Coordinator mute. |
| `SetRepeat` | Repeat mode on the speaker. |
| `SetShuffle` | Shuffle flag. When enabling shuffle while playing/paused, the tail after the on-deck track is shuffled (current and next stay put). Cloud Queue is refreshed. |
| `SetCrossfade` | Crossfade on the speaker. |

```json
{ "targetId": "RINCON_TESTPLAYER1", "command": "Pause" }
```

```json
{ "targetId": "RINCON_TESTPLAYER1", "command": "Seek", "positionTicks": 300000000 }
```

```json
{ "targetId": "RINCON_TESTPLAYER1", "command": "SetVolume", "volume": 25 }
```

```json
{ "targetId": "RINCON_TESTPLAYER1", "command": "SetRepeat", "repeat": "All" }
```

**200** `QueueResponse`

| Status | `error` |
| --- | --- |
| 400 | `UnknownCommand`, `InvalidTarget`, `PublishedUrlInvalid` (Play/Next/Previous only) |
| 403 | `PluginDisabled`, `LanAuthRequired` |
| 404 | `PlayerNotFound` |
| 409 | speaker control errors |

Unknown `command` values are rejected. There is no volume-up/down; compute an absolute `SetVolume`.

---

## Cloud Queue

After a successful Play, the plugin tells the coordinator to load Cloud Queue from the **published base URL**:

```
{publishedBase}/Sonos/queue/{coordinatorId}/v2.3/
```

The speaker then GETs `context`, `itemWindow`, and `version`, and streams audio/artwork from the tokenized URLs inside those payloads. Clients keep using `GET /Sonos/Queue` and `POST /Sonos/Playstate`; they do not poll Cloud Queue themselves.

If LAN Cloud Queue cannot load (`NotSupported`, `PlayerUnavailable`, or `LanAuthRequired` on the load path), Play falls back to SOAP `SetAVTransportURI` of `/Sonos/stream/{token}`. Those codes still surface for other commands. Grouping while a Jellyfin queue is playing reloads Cloud Queue at the current playhead.

Killing the Jellyfin client does not stop the speaker; the speaker keeps fetching stream URLs until pause/stop or token expiry.

### `GET|HEAD /Sonos/stream/{token}`

Tokenized audio. `HEAD` returns `Content-Type`, `Content-Length`, and `Accept-Ranges: bytes` with an empty body. `GET` supports HTTP Range.

| Direct play | `Content-Type` |
| --- | --- |
| FLAC | `audio/flac` |
| MP3 | `audio/mpeg` |
| AAC / M4A / MP4 | `audio/mp4` |
| OGG | `audio/ogg` |

Transcoded files are cached (ffmpeg) with a known length so the speaker can seek.

| Status | `error` |
| --- | --- |
| 403 | `InvalidToken` |
| 404 | `ItemNotFound` |
| 410 | `StreamExpired` |

### `GET|HEAD /Sonos/image/{token}`

Primary artwork for the token’s library item, then album, then parents. `HEAD`/`GET` same auth as stream.

| File | `Content-Type` |
| --- | --- |
| `.png` | `image/png` |
| `.gif` | `image/gif` |
| `.webp` and other | `image/jpeg` |

| Status | Meaning |
| --- | --- |
| 403 | `InvalidToken` |
| 404 | item or image file missing (plain 404, not always a problem body) |
| 410 | `StreamExpired` |

### `/Sonos/queue/{playerOrGroupId}/v2.3/...`

`{playerOrGroupId}` is the coordinator RINCON (URL-encoded) or a group id. JSON matches the Sonos Cloud Queue v2.3 shape, not the client `QueueResponse`.

#### `GET .../context`

Container metadata, playback policies, and versions.

```json
{
  "container": {
    "name": "Down the Way",
    "type": "trackList",
    "id": "RINCON_TESTPLAYER1",
    "service": { "name": "Jellyfin", "id": "jellyfin" },
    "imageUrl": "http://192.0.2.10:8096/Sonos/image/{token}"
  },
  "playbackPolicies": {
    "canSkip": true,
    "canSkipToItem": true,
    "canSkipBack": true,
    "limitedSkips": false,
    "canSeek": true,
    "canPause": true,
    "canStop": true,
    "canRepeat": true,
    "canRepeatOne": true,
    "canCrossfade": false,
    "canShuffle": true,
    "showNNextTracks": 10,
    "showNPreviousTracks": 10
  },
  "reports": {
    "sendUpdateAfterMillis": 0,
    "periodicIntervalMillis": 0,
    "sendPlaybackActions": false
  },
  "contextVersion": "1",
  "queueVersion": "1710000000000"
}
```

Progress reports are disabled (`periodicIntervalMillis: 0`). The plugin polls transport itself.

#### `GET .../itemWindow`

Window around a queue item.

| Query | Default | Description |
| --- | --- | --- |
| `itemId` | (start of queue) | Center `queueItemId`. Unknown id → index 0. |
| `previousWindowSize` | 10 | Items before center (≥ 0). |
| `upcomingWindowSize` | 10 | Items after center (≥ 0). |
| `reason` | — | Sonos reason string; logged only when verbose protocol logging is on. |

```json
{
  "items": [
    {
      "id": "a1b2c3d4e5f64789a0b1c2d3e4f50617",
      "deleted": false,
      "policies": {
        "canSkip": true,
        "canSkipToItem": true,
        "canSkipBack": true,
        "canCrossfade": false
      },
      "track": {
        "type": "track",
        "mediaUrl": "http://192.0.2.10:8096/Sonos/stream/{token}",
        "contentType": "audio/flac",
        "durationMillis": 243000,
        "name": "Hold On",
        "imageUrl": "http://192.0.2.10:8096/Sonos/image/{token}",
        "service": { "name": "Jellyfin", "id": "jellyfin" },
        "artist": { "name": "Angus & Julia Stone" },
        "album": {
          "name": "Down the Way",
          "artist": { "name": "Angus & Julia Stone" }
        }
      }
    }
  ],
  "includesBeginningOfQueue": true,
  "includesEndOfQueue": true,
  "contextVersion": "1",
  "queueVersion": "1710000000000"
}
```

`canSkip` / `canSkipToItem` are `false` on the last item so the speaker does not skip off the end.

#### `GET .../version`

```json
{
  "contextVersion": "1",
  "queueVersion": "1710000000000"
}
```

#### `POST|PUT .../timePlayed`

Speaker playback-progress hook. **204 No Content**; the body is ignored.

| Status | `error` (context / itemWindow / version) |
| --- | --- |
| 400 | `PublishedUrlInvalid` |
| 404 | `PlayerNotFound` |

---

## Web assets

Anonymous, cache `public, max-age=300`. Served for the injected jellyfin-web UI.

| Path | Type |
| --- | --- |
| `GET /Sonos/web/sonos-client.js` | `application/javascript` |
| `GET /Sonos/web/sonos-client.css` | `text/css` |
| `GET /Sonos/web/player-handoff.js` | `application/javascript` |

**404** if an embedded resource is missing.

---

## Typical client flows

### List rooms

```bash
curl -sS -H "Authorization: MediaBrowser Token=${JELLYFIN_API_KEY}" \
  http://192.0.2.10:8096/Sonos/Players
```

Wait up to ~30 seconds after setting seed IPs (or restart Jellyfin) if the list is empty.

### Play tracks

Resolve audio item GUIDs from the Jellyfin Items API, then:

```bash
curl -sS -H "Authorization: MediaBrowser Token=${JELLYFIN_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"targetId":"RINCON_TESTPLAYER1","itemIds":["aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"],"startIndex":0,"startPositionTicks":0}' \
  http://192.0.2.10:8096/Sonos/Queue/Play
```

### Poll and control

```bash
curl -sS -H "Authorization: MediaBrowser Token=${JELLYFIN_API_KEY}" \
  "http://192.0.2.10:8096/Sonos/Queue?targetId=RINCON_TESTPLAYER1"

curl -sS -H "Authorization: MediaBrowser Token=${JELLYFIN_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"targetId":"RINCON_TESTPLAYER1","command":"Pause"}' \
  http://192.0.2.10:8096/Sonos/Playstate
```

### Group then play

`POST /Sonos/Groups` with coordinator + members, then `POST /Sonos/Queue/Play` with `targetId` set to the coordinator (or the new `groups[].id`).

---

## Play To (not this HTTP API)

Jellyfin **Play To** / session messages (`Play`, `Playstate`, `GeneralCommand`) are mapped onto the same playback service as these routes. That path uses the plugin **Default user** when the session has no controlling user. It is not a REST surface; remote-control clients should call `/Sonos` directly if they are not a Jellyfin session controller.

---

## Stability

Path prefix `/Sonos`, JSON field names, playstate command names, and `error` codes are the compatibility surface. Stream token format, Cloud Queue `contextVersion` strings, and `queueVersion` values are opaque. Do not parse tokens. Jellyfin clients should call the authenticated player, group, queue, and playstate routes, not `/Sonos/stream`, `/Sonos/image`, or `/Sonos/queue/...`.
