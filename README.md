# Jellyfin Sonos Plugin

A Jellyfin **10.11** plugin that discovers Sonos **S2** speakers on the LAN and enables direct streaming from Jellyfin to Sonos. It includes auto speaker discovery, grouping, updates to the Jellyfin web UI and a comprehensive API.

![](assets/jellyfin-plugin-sonos.png)

## Target server

|                 | Target                                                |
| --------------- | ----------------------------------------------------- |
| Jellyfin        | 10.11.x (`targetAbi` 10.11.0.0)                       |
| Packages        | `Jellyfin.Controller` / `Jellyfin.Model` **10.11.11** |
| Framework       | `net9.0`                                              |
| Sideload folder | `$JELLYFIN_ROOT/data/plugins/Sonos_<version>/`        |

## Installation

### Plugin repository

1. Dashboard → Plugins → Repositories → Add, and paste:

   `https://raw.githubusercontent.com/adamdunkley/jellyfin-plugin-sonos/main/manifest.json`

2. Dashboard → Plugins → Catalog → find **Sonos** → Install.
3. Restart Jellyfin.

## Building

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet build -c Release
dotnet test -c Release
```

### Sideloading

Set `JELLYFIN_ROOT` to the Jellyfin config directory (the folder that contains `data/plugins`), then run:

```bash
JELLYFIN_ROOT=/path/to/jellyfin ./scripts/sideload.sh
```

This builds the plugin and copies `Jellyfin.Plugin.Sonos.dll` and `meta.json` to `$JELLYFIN_ROOT/data/plugins/Sonos_<version>/`. Restart Jellyfin afterwards and check Dashboard → Plugins for the updated plugin.

## HTTP API

Authenticated Jellyfin clients (official apps, third-party UIs, scripts) can list S2 speakers, group rooms, play the music library, and control transport under `/Sonos`. The injected web UI uses the same routes. Contract, auth, and examples: **[API.md](API.md)**.

## Published base URL

Speakers must HTTP-GET audio and Cloud Queue from an address they can route to. Set **Published base URL** to a LAN HTTP origin the speakers can reach, including any server base path, for example:

```
http://192.0.2.10:8096/media
```

Playback fails with `PublishedUrlInvalid` when this is missing, loopback (`127.0.0.0/8`), link-local (`169.254/8`), or a Docker-bridge address (`172.16.0.0/12`).

## LAN authentication (403)

Current Sonos apps expose a privacy toggle for third-party **LAN** integrations. If players appear but commands return `LanAuthRequired` / HTTP 403:

1. In the Sonos app, allow third-party LAN control (disable the LAN authentication / privacy lock).
2. Confirm the speaker is S2 and reachable on TCP 1443 / 1400 from the Jellyfin host.

No Sonos developer key is required. The LAN API uses the well-known local token (same as [aiosonos](https://github.com/music-assistant/aiosonos)).

## Docker

If Jellyfin runs in Docker, multicast discovery usually needs host networking or seed IPs inputting manually in the config (see below) to discover Sonos speakers.

## DLNA coexistence

This plugin does not use generic DLNA Play To for S2. The official Jellyfin DLNA plugin may still list the same speakers as separate Play To devices.

## Web UI

Hard-refresh jellyfin-web after install so the injected client loads.

Speakers appear in the usual **Cast / Play on** menu. A speaker button next to the cast icon (and on the now-playing bar) groups rooms. Stock play/pause/skip/seek/volume controls the Sonos queue; set "Play locally" or disconnect through the cast menu to play in the browser again.

## Config page

Dashboard → Plugins → Sonos:

| Setting                   | Purpose                                            |
| ------------------------- | -------------------------------------------------- |
| Enabled                   | Stop discovery and playback                        |
| Default user              | Fallback user when there is no HTTP user (Play To) |
| Published base URL        | URL speakers use (required for playback)           |
| Seed player IPs           | Unicast discovery when multicast fails             |
| Preferred transcode codec | `flac` (default) / `aac`                           |
| Ignored player IDs        | Never expose or control                            |
| Verbose protocol logging  | Sonos plugin log to the Jellyfin log               |

## LLM disclosure

This plugin was developed with assistance from Large Language Models but all output has been audited by the repo creator.
