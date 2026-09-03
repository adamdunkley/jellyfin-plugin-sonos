using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Discovery;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// LAN Control WebSocket client (aiosonos protocol port).
/// </summary>
public sealed class LanControlClient : IAsyncDisposable
{
    private readonly ILogger<LanControlClient> _logger;
    private readonly CoordinatorGate _gate;
    private readonly ConcurrentDictionary<string, PlayerConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="LanControlClient"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="gate">Per-coordinator lock.</param>
    public LanControlClient(ILogger<LanControlClient> logger, CoordinatorGate gate)
    {
        _logger = logger;
        _gate = gate;
    }

    /// <summary>
    /// Gets a value indicating whether a Cloud Queue session is open for the player.
    /// </summary>
    /// <param name="playerId">Coordinator id.</param>
    /// <returns>True when a session id is known.</returns>
    public bool HasSession(string playerId)
        => _connections.TryGetValue(playerId, out var conn)
           && conn.Socket.State == WebSocketState.Open
           && !string.IsNullOrEmpty(conn.SessionId);

    /// <summary>
    /// Ensures a websocket is connected for the player.
    /// </summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connected.</returns>
    public async Task<bool> TryConnectAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(player.WebsocketUrl) && string.IsNullOrEmpty(player.Ip))
        {
            return false;
        }

        if (_connections.TryGetValue(player.Id, out var existing) && existing.Socket.State == WebSocketState.Open)
        {
            return true;
        }

        existing?.Dispose();
        var url = string.IsNullOrEmpty(player.WebsocketUrl)
            ? $"wss://{player.Ip}:1443/websocket/api"
            : player.WebsocketUrl;

        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol(SonosConstants.WebsocketProtocol);
        ws.Options.SetRequestHeader("X-Sonos-Api-Key", SonosConstants.LocalApiToken);
        ws.Options.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await ws.ConnectAsync(new Uri(url), cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LAN websocket connect failed for {Player}", player.Id);
            ws.Dispose();
            if (ex is WebSocketException { WebSocketErrorCode: WebSocketError.NotAWebSocket }
                || ex.Message.Contains("403", StringComparison.Ordinal))
            {
                throw new SonosControlException("LanAuthRequired", "Speaker returned 403", 403);
            }

            return false;
        }

        var connection = new PlayerConnection(ws, _logger);
        _connections[player.Id] = connection;
        // Start the receive loop now so ReceiveAsync is pending before the first command.
        // Do not use the HTTP request token: the socket outlives a single Play call.
        connection.ListenTask = ListenAsync(connection, connection.Lifetime.Token);
        _logger.LogInformation("LAN websocket connected to {Player} at {Url}", player.Name, url);
        return true;
    }

    /// <summary>
    /// Joins or creates a playback session and subscribes to playback events.
    /// </summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="appContext">Calling user id (shared session key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task EnsureSessionAsync(DiscoveredPlayer player, string appContext, CancellationToken cancellationToken)
    {
        return _gate.RunAsync(player.Id, () => EnsureSessionCoreAsync(player, appContext, forceCreate: false, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Loads a Cloud Queue on the open session.
    /// </summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="request">Load parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task LoadCloudQueueAsync(DiscoveredPlayer player, LoadCloudQueueRequest request, CancellationToken cancellationToken)
    {
        var appContext = request.Extra is not null && request.Extra.TryGetValue("appContext", out var ctx)
            ? ctx
            : "default";
        return _gate.RunAsync(
            player.Id,
            async () =>
            {
                await EnsureSessionCoreAsync(player, appContext, forceCreate: false, cancellationToken).ConfigureAwait(false);
                try
                {
                    await LoadCloudQueueCoreAsync(player, request, cancellationToken).ConfigureAwait(false);
                }
                catch (SonosControlException ex) when (ex.IsMissingPlaybackSession())
                {
                    var conn = RequireConnection(player);
                    conn.InvalidateSession();
                    _logger.LogInformation(
                        "Cloud Queue session missing on {Player}; creating a new session and retrying load",
                        player.Name);
                    await EnsureSessionCoreAsync(player, appContext, forceCreate: true, cancellationToken).ConfigureAwait(false);
                    await LoadCloudQueueCoreAsync(player, request, cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Asks the player to refetch the Cloud Queue window.
    /// </summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task RefreshCloudQueueAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => SessionCommandAsync(player, "playbackSession", "refreshCloudQueue", new JsonObject(), cancellationToken);

    /// <summary>Playback play.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task PlayAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => GroupCommandAsync(player, "playback", "play", new JsonObject(), cancellationToken);

    /// <summary>Playback pause.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task PauseAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => GroupCommandAsync(player, "playback", "pause", new JsonObject(), cancellationToken);

    /// <summary>Skip next.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task NextAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => GroupCommandAsync(player, "playback", "skipToNextTrack", new JsonObject(), cancellationToken);

    /// <summary>Skip previous.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task PreviousAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => GroupCommandAsync(player, "playback", "skipToPreviousTrack", new JsonObject(), cancellationToken);

    /// <summary>Seek within the current track.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="position">Position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SeekAsync(DiscoveredPlayer player, TimeSpan position, CancellationToken cancellationToken)
    {
        var millis = (long)position.TotalMilliseconds;
        return GroupCommandAsync(
            player,
            "playback",
            "seek",
            new JsonObject { ["positionMillis"] = millis },
            cancellationToken);
    }

    /// <summary>Sets group volume.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="volume">0-100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SetVolumeAsync(DiscoveredPlayer player, int volume, CancellationToken cancellationToken)
    {
        volume = Math.Clamp(volume, 0, 100);
        return GroupCommandAsync(
            player,
            "groupVolume",
            "setVolume",
            new JsonObject { ["volume"] = volume },
            cancellationToken);
    }

    /// <summary>Sets mute.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="muted">Mute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SetMuteAsync(DiscoveredPlayer player, bool muted, CancellationToken cancellationToken)
        => GroupCommandAsync(player, "groupVolume", "setMute", new JsonObject { ["muted"] = muted }, cancellationToken);

    /// <summary>Reads volume and mute.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Volume snapshot.</returns>
    public async Task<(int Volume, bool Muted)> GetVolumeAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
    {
        var data = await GroupCommandAsync(player, "groupVolume", "getVolume", new JsonObject(), cancellationToken).ConfigureAwait(false);
        return TransportSnapshot.VolumeFromStatus(data);
    }

    /// <summary>Reads transport state.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Snapshot.</returns>
    public async Task<TransportSnapshot> GetTransportAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
    {
        var data = await GroupCommandAsync(player, "playback", "getPlaybackStatus", new JsonObject(), cancellationToken).ConfigureAwait(false);
        var (volume, muted) = await GetVolumeAsync(player, cancellationToken).ConfigureAwait(false);
        var stateRaw = data is JsonObject obj ? obj["playbackState"]?.GetValue<string>() : null;
        var position = TransportSnapshot.PositionTicksFromStatus(data);

        var state = stateRaw switch
        {
            "PLAYBACK_STATE_PLAYING" => Api.Models.PlaybackState.Playing,
            "PLAYBACK_STATE_PAUSED" => Api.Models.PlaybackState.Paused,
            "PLAYBACK_STATE_BUFFERING" => Api.Models.PlaybackState.Transitioning,
            _ => Api.Models.PlaybackState.Stopped
        };
        return new TransportSnapshot
        {
            State = state,
            PositionTicks = position,
            Volume = volume,
            Muted = muted,
            CurrentItemId = TransportSnapshot.ItemIdFromStatus(data)
        };
    }

    /// <summary>Sets play modes.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="repeat">None, All, or One.</param>
    /// <param name="shuffle">Shuffle.</param>
    /// <param name="crossfade">Crossfade.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SetPlayModesAsync(DiscoveredPlayer player, string repeat, bool shuffle, bool crossfade, CancellationToken cancellationToken)
    {
        PlayModeMapper.ToLanFlags(repeat, out var repeatAll, out var repeatOne);
        var modes = new JsonObject
        {
            ["repeat"] = repeatAll,
            ["repeatOne"] = repeatOne,
            ["shuffle"] = shuffle,
            ["crossfade"] = crossfade
        };
        return GroupCommandAsync(player, "playback", "setPlayModes", new JsonObject { ["playModes"] = modes }, cancellationToken);
    }

    /// <summary>Skips to a Cloud Queue item.</summary>
    /// <param name="player">Coordinator.</param>
    /// <param name="itemId">Queue item id.</param>
    /// <param name="positionMillis">Offset within the item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SkipToItemAsync(DiscoveredPlayer player, string itemId, int positionMillis, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["itemId"] = itemId,
            ["playOnCompletion"] = true
        };
        if (positionMillis > 0)
        {
            body["positionMillis"] = positionMillis;
        }

        return SessionCommandAsync(player, "playbackSession", "skipToItem", body, cancellationToken);
    }

    /// <summary>Creates a group on the household.</summary>
    /// <param name="player">Any player in the household (used for the websocket).</param>
    /// <param name="playerIds">Player ids; first is coordinator.</param>
    /// <param name="musicContextGroupId">Optional group whose playback should continue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new group.</returns>
    public Task<GroupCommandResult> CreateGroupAsync(
        DiscoveredPlayer player,
        IReadOnlyList<string> playerIds,
        string? musicContextGroupId,
        CancellationToken cancellationToken)
    {
        return _gate.RunAsync(
            player.Id,
            async () =>
            {
                await EnsureConnectedCoreAsync(player, cancellationToken).ConfigureAwait(false);
                var conn = RequireConnection(player);
                if (string.IsNullOrEmpty(conn.HouseholdId))
                {
                    throw new SonosControlException("PlayerUnavailable", "No household id for grouping on " + player.Name);
                }

                var body = new JsonObject { ["playerIds"] = ToJsonArray(playerIds) };
                if (!string.IsNullOrEmpty(musicContextGroupId))
                {
                    body["musicContextGroupId"] = musicContextGroupId;
                }

                var data = await SendOnAsync(
                    conn,
                    "groups",
                    "createGroup",
                    new JsonObject { ["householdId"] = conn.HouseholdId },
                    body,
                    cancellationToken).ConfigureAwait(false);
                var result = GroupCommandResult.FromLan(data, playerIds[0], player.GroupId ?? player.Id);
                return await AfterGroupChangeAsync(conn, player, result, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>Modifies group membership.</summary>
    /// <param name="player">Coordinator or household member.</param>
    /// <param name="groupId">Group id.</param>
    /// <param name="playerIdsToAdd">Players to add.</param>
    /// <param name="playerIdsToRemove">Players to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated group.</returns>
    public Task<GroupCommandResult> ModifyGroupMembersAsync(
        DiscoveredPlayer player,
        string groupId,
        IReadOnlyList<string> playerIdsToAdd,
        IReadOnlyList<string> playerIdsToRemove,
        CancellationToken cancellationToken)
    {
        return _gate.RunAsync(
            player.Id,
            async () =>
            {
                await EnsureConnectedCoreAsync(player, cancellationToken).ConfigureAwait(false);
                var conn = RequireConnection(player);
                var data = await SendOnAsync(
                    conn,
                    "groups",
                    "modifyGroupMembers",
                    new JsonObject { ["groupId"] = groupId },
                    new JsonObject
                    {
                        ["playerIdsToAdd"] = ToJsonArray(playerIdsToAdd),
                        ["playerIdsToRemove"] = ToJsonArray(playerIdsToRemove)
                    },
                    cancellationToken).ConfigureAwait(false);
                var result = GroupCommandResult.FromLan(data, player.Id, groupId);
                return await AfterGroupChangeAsync(conn, player, result, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private static JsonArray ToJsonArray(IReadOnlyList<string> ids)
    {
        var array = new JsonArray();
        foreach (var id in ids)
        {
            array.Add(id);
        }

        return array;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }

        _connections.Clear();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task EnsureConnectedCoreAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
    {
        if (!await TryConnectAsync(player, cancellationToken).ConfigureAwait(false)
            || !_connections.TryGetValue(player.Id, out var conn))
        {
            throw new SonosControlException("PlayerUnavailable", "Could not open LAN Control websocket to " + player.Name);
        }

        if (string.IsNullOrEmpty(conn.HouseholdId))
        {
            conn.HouseholdId = player.HouseholdId ?? string.Empty;
        }

        if (string.IsNullOrEmpty(conn.GroupId))
        {
            conn.GroupId = player.GroupId ?? string.Empty;
            if (!string.IsNullOrEmpty(conn.HouseholdId))
            {
                var groups = await SendOnAsync(
                    conn,
                    "groups",
                    "getGroups",
                    new JsonObject { ["householdId"] = conn.HouseholdId },
                    new JsonObject { ["includeDeviceInfo"] = false },
                    cancellationToken).ConfigureAwait(false);
                conn.GroupId = ResolveGroupId(groups, player) ?? conn.GroupId;
            }
        }

        if (string.IsNullOrEmpty(conn.GroupId))
        {
            conn.GroupId = player.Id;
        }
    }

    private async Task<GroupCommandResult> AfterGroupChangeAsync(
        PlayerConnection conn,
        DiscoveredPlayer player,
        GroupCommandResult result,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(result.GroupId))
        {
            conn.GroupId = result.GroupId;
            player.GroupId = result.GroupId;
        }

        conn.InvalidateSession();
        conn.Subscribed = false;

        if (string.IsNullOrEmpty(conn.HouseholdId))
        {
            return result;
        }

        try
        {
            var groups = await SendOnAsync(
                conn,
                "groups",
                "getGroups",
                new JsonObject { ["householdId"] = conn.HouseholdId },
                new JsonObject { ["includeDeviceInfo"] = false },
                cancellationToken).ConfigureAwait(false);
            var resolved = ResolveGroupId(groups, player);
            if (!string.IsNullOrEmpty(resolved))
            {
                conn.GroupId = resolved;
                player.GroupId = resolved;
                return new GroupCommandResult
                {
                    GroupId = resolved,
                    CoordinatorId = result.CoordinatorId,
                    PlayerIds = result.PlayerIds
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "getGroups after grouping failed for {Player}", player.Id);
        }

        return result;
    }

    private async Task LoadCloudQueueCoreAsync(DiscoveredPlayer player, LoadCloudQueueRequest request, CancellationToken cancellationToken)
    {
        var conn = RequireConnection(player);
        var body = new JsonObject
        {
            ["queueBaseUrl"] = request.QueueBaseUrl,
            ["itemId"] = request.ItemId,
            ["queueVersion"] = request.QueueVersion,
            ["playOnCompletion"] = true
        };
        if (request.PositionMillis > 0)
        {
            body["positionMillis"] = request.PositionMillis;
        }

        if (!string.IsNullOrEmpty(request.HttpAuthorization))
        {
            body["httpAuthorization"] = request.HttpAuthorization;
        }

        if (request.TrackMetadata is not null)
        {
            body["trackMetadata"] = request.TrackMetadata.DeepClone();
        }
        else if (!string.IsNullOrEmpty(request.FirstMediaUrl))
        {
            var track = new JsonObject
            {
                ["type"] = "track",
                ["mediaUrl"] = request.FirstMediaUrl,
                ["name"] = request.FirstTrackName ?? string.Empty
            };
            body["trackMetadata"] = track;
        }

        await SendOnAsync(conn, "playbackSession", "loadCloudQueue", new JsonObject { ["sessionId"] = conn.SessionId }, body, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureSessionCoreAsync(DiscoveredPlayer player, string appContext, bool forceCreate, CancellationToken cancellationToken)
    {
        await EnsureConnectedCoreAsync(player, cancellationToken).ConfigureAwait(false);
        var conn = RequireConnection(player);

        if (!conn.Subscribed)
        {
            await SendOnAsync(conn, "playback", "subscribe", new JsonObject { ["groupId"] = conn.GroupId }, new JsonObject(), cancellationToken)
                .ConfigureAwait(false);
            await SendOnAsync(conn, "playbackMetadata", "subscribe", new JsonObject { ["groupId"] = conn.GroupId }, new JsonObject(), cancellationToken)
                .ConfigureAwait(false);
            conn.Subscribed = true;
        }

        if (!forceCreate && !string.IsNullOrEmpty(conn.SessionId))
        {
            try
            {
                await EnsurePlaybackSessionSubscribedAsync(conn, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SonosControlException ex) when (ex.IsMissingPlaybackSession())
            {
                conn.InvalidateSession();
            }
        }

        var sessionBody = new JsonObject
        {
            ["appId"] = SonosConstants.AppId,
            ["appContext"] = appContext
        };
        JsonNode? session;
        if (forceCreate)
        {
            session = await SendOnAsync(
                conn,
                "playbackSession",
                "createSession",
                new JsonObject { ["groupId"] = conn.GroupId },
                sessionBody,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                session = await SendOnAsync(
                    conn,
                    "playbackSession",
                    "joinOrCreateSession",
                    new JsonObject { ["groupId"] = conn.GroupId },
                    sessionBody,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (SonosControlException)
            {
                session = await SendOnAsync(
                    conn,
                    "playbackSession",
                    "createSession",
                    new JsonObject { ["groupId"] = conn.GroupId },
                    sessionBody,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        conn.SessionId = session is JsonObject s
            ? s["sessionId"]?.GetValue<string>() ?? s["id"]?.GetValue<string>() ?? string.Empty
            : string.Empty;
        conn.SessionSubscribed = false;
        if (string.IsNullOrEmpty(conn.SessionId))
        {
            throw new SonosControlException("PlayerUnavailable", "LAN Control did not return a session id");
        }

        await EnsurePlaybackSessionSubscribedAsync(conn, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsurePlaybackSessionSubscribedAsync(PlayerConnection conn, CancellationToken cancellationToken)
    {
        if (conn.SessionSubscribed || string.IsNullOrEmpty(conn.SessionId))
        {
            return;
        }

        await SendOnAsync(
            conn,
            "playbackSession",
            "subscribe",
            new JsonObject { ["sessionId"] = conn.SessionId },
            new JsonObject(),
            cancellationToken).ConfigureAwait(false);
        conn.SessionSubscribed = true;
    }

    private static string? ResolveGroupId(JsonNode? groups, DiscoveredPlayer player)
    {
        if (groups is not JsonObject obj || obj["groups"] is not JsonArray array)
        {
            return player.GroupId;
        }

        foreach (var node in array)
        {
            if (node is not JsonObject group)
            {
                continue;
            }

            var id = group["id"]?.GetValue<string>();
            var coordinator = group["coordinatorId"]?.GetValue<string>();
            if (string.Equals(coordinator, player.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, player.GroupId, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }

            if (group["playerIds"] is JsonArray ids)
            {
                foreach (var pid in ids)
                {
                    if (string.Equals(pid?.GetValue<string>(), player.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        return id;
                    }
                }
            }
        }

        return player.GroupId;
    }

    private Task<JsonNode?> GroupCommandAsync(
        DiscoveredPlayer player,
        string ns,
        string command,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        return _gate.RunAsync(
            player.Id,
            async () =>
            {
                var conn = RequireConnection(player);
                return await SendOnAsync(conn, ns, command, new JsonObject { ["groupId"] = conn.GroupId }, body, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    private Task<JsonNode?> SessionCommandAsync(
        DiscoveredPlayer player,
        string ns,
        string command,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        return _gate.RunAsync(
            player.Id,
            async () =>
            {
                var conn = RequireConnection(player);
                return await SendOnAsync(conn, ns, command, new JsonObject { ["sessionId"] = conn.SessionId }, body, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    private PlayerConnection RequireConnection(DiscoveredPlayer player)
    {
        if (!_connections.TryGetValue(player.Id, out var conn) || conn.Socket.State != WebSocketState.Open)
        {
            throw new SonosControlException("PlayerUnavailable", "LAN Control websocket is not connected to " + player.Name);
        }

        return conn;
    }

    private async Task ListenAsync(PlayerConnection connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (connection.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await connection.Socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
                }
                while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(message.ToArray());
                connection.HandleMessage(json);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LAN websocket listen ended");
        }
        catch (OperationCanceledException)
        {
            // Connection disposed or plugin stopping.
        }
    }

    private static async Task<JsonNode?> SendOnAsync(
        PlayerConnection connection,
        string ns,
        string command,
        JsonObject pathParams,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        return await connection.SendCommandAsync(ns, command, pathParams, body, cancellationToken).ConfigureAwait(false);
    }

    private sealed class PlayerConnection : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
        private readonly ILogger _logger;

        public PlayerConnection(ClientWebSocket socket, ILogger logger)
        {
            Socket = socket;
            _logger = logger;
        }

        public ClientWebSocket Socket { get; }

        public CancellationTokenSource Lifetime { get; } = new();

        public Task? ListenTask { get; set; }

        public string SessionId { get; set; } = string.Empty;

        public string GroupId { get; set; } = string.Empty;

        public string HouseholdId { get; set; } = string.Empty;

        public bool Subscribed { get; set; }

        public bool SessionSubscribed { get; set; }

        public void InvalidateSession()
        {
            SessionId = string.Empty;
            SessionSubscribed = false;
        }

        public async Task<JsonNode?> SendCommandAsync(
            string ns,
            string command,
            JsonObject pathParams,
            JsonObject body,
            CancellationToken cancellationToken)
        {
            var cmdId = Guid.NewGuid().ToString("N");
            var header = new JsonObject
            {
                ["namespace"] = ns + ":1",
                ["command"] = command,
                ["cmdId"] = cmdId
            };
            foreach (var kv in pathParams)
            {
                header[kv.Key] = kv.Value is null ? null : JsonNode.Parse(kv.Value.ToJsonString());
            }

            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[cmdId] = tcs;
            var payload = new JsonArray { header.DeepClone(), body.DeepClone() };
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            // aiosonos sends JSON as binary frames; speakers ignore or drop text frames.
            await Socket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
            if (Plugin.Instance?.Configuration.VerboseProtocolLogging == true)
            {
                _logger.LogInformation("LAN WS send {Namespace}.{Command} cmdId={CmdId}", ns, command, cmdId);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var reg = timeout.Token.Register(() => tcs.TrySetException(
                new SonosControlException("PlayerUnavailable", command + " timed out")));
            return await tcs.Task.ConfigureAwait(false);
        }

        public void HandleMessage(string json)
        {
            try
            {
                var node = JsonNode.Parse(json);
                if (node is not JsonArray { Count: >= 1 } array)
                {
                    return;
                }

                var header = array[0] as JsonObject;
                var data = array.Count > 1 ? array[1] : null;
                var cmdId = header?["cmdId"]?.GetValue<string>();
                var command = header?["command"]?.GetValue<string>() ?? header?["type"]?.GetValue<string>();
                if (Plugin.Instance?.Configuration.VerboseProtocolLogging == true)
                {
                    _logger.LogInformation("LAN WS recv command={Command} cmdId={CmdId} pending={Pending}", command, cmdId, !string.IsNullOrEmpty(cmdId) && _pending.ContainsKey(cmdId));
                }

                var headerType = header?["type"]?.GetValue<string>();
                var obj = data as JsonObject;
                var code = obj?["errorCode"]?.GetValue<string>() ?? headerType ?? string.Empty;
                var reason = obj?["reason"]?.GetValue<string>() ?? obj?["errorCode"]?.GetValue<string>() ?? obj?["message"]?.GetValue<string>() ?? code;
                if (ShouldInvalidateCachedSession(headerType, code, reason))
                {
                    InvalidateSession();
                }

                if (string.IsNullOrEmpty(cmdId) || !_pending.TryRemove(cmdId, out var tcs))
                {
                    return;
                }

                if (string.Equals(headerType, "sessionError", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerType, "globalError", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerType, "playbackError", StringComparison.OrdinalIgnoreCase)
                    || (data is JsonObject errObj && errObj.ContainsKey("errorCode")))
                {
                    if (string.Equals(code, "ERROR_CLOUD_QUEUE_SERVICE_ERROR", StringComparison.OrdinalIgnoreCase))
                    {
                        tcs.TrySetException(new SonosControlException(code, reason));
                        return;
                    }

                    if (reason.Contains("403", StringComparison.Ordinal)
                        || (string.Equals(code, "ERROR_COMMAND_FAILED", StringComparison.OrdinalIgnoreCase)
                            && reason.Contains("auth", StringComparison.OrdinalIgnoreCase)))
                    {
                        tcs.TrySetException(new SonosControlException("LanAuthRequired", reason, 403));
                        return;
                    }

                    tcs.TrySetException(new SonosControlException(string.IsNullOrEmpty(code) ? "ERROR" : code, reason));
                    return;
                }

                if (header?["success"]?.GetValue<bool>() == false)
                {
                    tcs.TrySetException(new SonosControlException("CommandFailed", header["response"]?.ToString() ?? "failed"));
                    return;
                }

                tcs.TrySetResult(data);
            }
            catch (Exception)
            {
                // Ignore malformed events.
            }
        }

        private static bool ShouldInvalidateCachedSession(string? headerType, string code, string reason)
        {
            if (string.Equals(code, "ERROR_SESSION_EVICTED", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (reason.Contains("no session", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(headerType, "sessionError", StringComparison.OrdinalIgnoreCase)
                   && new SonosControlException(string.IsNullOrEmpty(code) ? "sessionError" : code, reason).IsMissingPlaybackSession();
        }

        public void Dispose()
        {
            Lifetime.Cancel();
            Socket.Dispose();
            Lifetime.Dispose();
        }
    }
}
