using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Api;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Control;
using Jellyfin.Plugin.Sonos.Discovery;
using Jellyfin.Plugin.Sonos.Queue;
using Jellyfin.Plugin.Sonos.Streaming;
using Jellyfin.Plugin.Sonos.Util;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos;

/// <summary>
/// Orchestrates library lookup, planning, tokens, logical queue, and speaker control.
/// </summary>
public sealed class SonosPlaybackService
{
    private static readonly TimeSpan PollMinInterval = TimeSpan.FromSeconds(1);

    private readonly TargetResolver _targets;
    private readonly PlayerRegistry _registry;
    private readonly LogicalQueueStore _queues;
    private readonly StreamTokenService _tokens;
    private readonly ISonosControlClient _control;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSources;
    private readonly ILogger<SonosPlaybackService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosPlaybackService"/> class.
    /// </summary>
    /// <param name="targets">Target resolver.</param>
    /// <param name="registry">Player registry.</param>
    /// <param name="queues">Logical queues.</param>
    /// <param name="tokens">Stream tokens.</param>
    /// <param name="control">Speaker control.</param>
    /// <param name="libraryManager">Library.</param>
    /// <param name="mediaSources">Media streams.</param>
    /// <param name="logger">Logger.</param>
    public SonosPlaybackService(
        TargetResolver targets,
        PlayerRegistry registry,
        LogicalQueueStore queues,
        StreamTokenService tokens,
        ISonosControlClient control,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSources,
        ILogger<SonosPlaybackService> logger)
    {
        _targets = targets;
        _registry = registry;
        _queues = queues;
        _tokens = tokens;
        _control = control;
        _libraryManager = libraryManager;
        _mediaSources = mediaSources;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the calling Jellyfin user, falling back to the configured default user.
    /// </summary>
    /// <param name="principal">HTTP user.</param>
    /// <param name="error">Problem when no user can be resolved.</param>
    /// <returns>The user, or null.</returns>
    public Guid? ResolveUserId(ClaimsPrincipal principal, out ObjectResult? error)
    {
        error = null;
        var claim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst("UserId")?.Value
                    ?? principal.FindFirst("Jellyfin-UserId")?.Value;
        if (Guid.TryParse(claim, out var userId) && userId != Guid.Empty)
        {
            return userId;
        }

        if (Guid.TryParse(Plugin.Instance?.Configuration.DefaultUserId, out var fallbackId) && fallbackId != Guid.Empty)
        {
            return fallbackId;
        }

        error = ProblemResults.Create(StatusCodes.Status400BadRequest, "UserRequired", "No Jellyfin user is available for library access");
        return null;
    }

    /// <summary>
    /// Replaces the queue and starts playback.
    /// </summary>
    /// <param name="request">Play request.</param>
    /// <param name="userId">Calling user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot or problem.</returns>
    public async Task<ActionResult> PlayAsync(PlayQueueRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (TryDisabled(out var disabled))
        {
            return disabled;
        }

        if (request.ItemIds is null || request.ItemIds.Count == 0)
        {
            return ProblemResults.Create(StatusCodes.Status400BadRequest, "InvalidRequest", "itemIds is required");
        }

        if (!_targets.TryResolve(request.TargetId, out var coordinator, out var error))
        {
            return error!;
        }

        if (!TryPublishedBase(out var published, out var publishedError))
        {
            return publishedError;
        }

        var built = TryBuildItems(request.ItemIds, userId, coordinator, out var items, out var buildError);
        if (!built)
        {
            return buildError!;
        }

        var startIndex = Math.Clamp(request.StartIndex, 0, items.Count - 1);
        var queue = _queues.Replace(coordinator.Id, items, startIndex, userId);
        queue.ContainerName = items.Count == 1 ? items[0].Name : items[0].Album;
        if (string.IsNullOrEmpty(queue.ContainerName))
        {
            queue.ContainerName = "Jellyfin";
        }

        try
        {
            await StartCurrentAsync(coordinator, queue, published, request.StartPositionTicks, cancellationToken).ConfigureAwait(false);
        }
        catch (SonosControlException ex)
        {
            return MapControlException(ex, coordinator);
        }

        return await GetQueueAsync(coordinator.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds items Next or Last.
    /// </summary>
    /// <param name="request">Add request.</param>
    /// <param name="userId">Calling user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot or problem.</returns>
    public async Task<ActionResult> AddAsync(AddQueueRequest request, Guid userId, CancellationToken cancellationToken)
    {
        if (TryDisabled(out var disabled))
        {
            return disabled;
        }

        if (request.ItemIds is null || request.ItemIds.Count == 0)
        {
            return ProblemResults.Create(StatusCodes.Status400BadRequest, "InvalidRequest", "itemIds is required");
        }

        if (!_targets.TryResolve(request.TargetId, out var coordinator, out var error))
        {
            return error!;
        }

        if (!TryPublishedBase(out var published, out var publishedError))
        {
            return publishedError;
        }

        if (!TryBuildItems(request.ItemIds, userId, coordinator, out var items, out var buildError))
        {
            return buildError!;
        }

        var queue = _queues.GetOrCreate(coordinator.Id);
        var next = string.Equals(request.Mode, "Next", StringComparison.OrdinalIgnoreCase);
        LogicalQueueStore.Add(queue, items, next);
        try
        {
            if (queue.UsesCloudQueue)
            {
                await _control.RefreshCloudQueueAsync(coordinator, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SonosControlException ex)
        {
            return MapControlException(ex, coordinator);
        }

        _ = published;
        return await GetQueueAsync(coordinator.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes items by queue item id.
    /// </summary>
    /// <param name="request">Remove request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot or problem.</returns>
    public async Task<ActionResult> RemoveAsync(RemoveQueueRequest request, CancellationToken cancellationToken)
    {
        if (TryDisabled(out var disabled))
        {
            return disabled;
        }

        if (!_targets.TryResolve(request.TargetId, out var coordinator, out var error))
        {
            return error!;
        }

        if (!_queues.TryGet(coordinator.Id, out var queue))
        {
            return ProblemResults.Create(StatusCodes.Status404NotFound, "QueueNotFound", "No queue for this target");
        }

        LogicalQueueStore.Remove(queue, request.QueueItemIds ?? []);
        try
        {
            if (queue.UsesCloudQueue)
            {
                await _control.RefreshCloudQueueAsync(coordinator, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SonosControlException ex)
        {
            return MapControlException(ex, coordinator);
        }

        return await GetQueueAsync(coordinator.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a queue item.
    /// </summary>
    /// <param name="request">Move request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot or problem.</returns>
    public async Task<ActionResult> MoveAsync(MoveQueueRequest request, CancellationToken cancellationToken)
    {
        if (TryDisabled(out var disabled))
        {
            return disabled;
        }

        if (!_targets.TryResolve(request.TargetId, out var coordinator, out var error))
        {
            return error!;
        }

        if (!_queues.TryGet(coordinator.Id, out var queue))
        {
            return ProblemResults.Create(StatusCodes.Status404NotFound, "QueueNotFound", "No queue for this target");
        }

        try
        {
            LogicalQueueStore.Move(queue, request.FromIndex, request.ToIndex);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ProblemResults.Create(StatusCodes.Status400BadRequest, "InvalidRequest", "fromIndex or toIndex is out of range");
        }

        try
        {
            if (queue.UsesCloudQueue)
            {
                await _control.RefreshCloudQueueAsync(coordinator, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SonosControlException ex)
        {
            return MapControlException(ex, coordinator);
        }

        return await GetQueueAsync(coordinator.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cheap queue poll.
    /// </summary>
    /// <param name="targetId">Player or group id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot or problem.</returns>
    public async Task<ActionResult> GetQueueAsync(string? targetId, CancellationToken cancellationToken)
    {
        if (!_targets.TryResolve(targetId, out var coordinator, out var error))
        {
            return error!;
        }

        var queue = _queues.GetOrCreate(coordinator.Id);
        await RefreshTransportIfDueAsync(coordinator, queue, cancellationToken).ConfigureAwait(false);
        lock (queue)
        {
            return new OkObjectResult(ToResponse(queue));
        }
    }

    /// <summary>
    /// Applies a playstate command.
    /// </summary>
    /// <param name="request">Command body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot or problem.</returns>
    public async Task<ActionResult> PlaystateAsync(PlaystateRequest request, CancellationToken cancellationToken)
    {
        if (TryDisabled(out var disabled))
        {
            return disabled;
        }

        if (!PlaystateCommands.IsKnown(request.Command))
        {
            return ProblemResults.Create(StatusCodes.Status400BadRequest, "UnknownCommand", "Unsupported playstate command");
        }

        if (!_targets.TryResolve(request.TargetId, out var coordinator, out var error))
        {
            return error!;
        }

        var queue = _queues.GetOrCreate(coordinator.Id);
        if (!TryPublishedBase(out var published, out var publishedError)
            && request.Command is "Play" or "Next" or "Previous")
        {
            return publishedError;
        }

        try
        {
            await ApplyPlaystateAsync(coordinator, queue, request, published, cancellationToken).ConfigureAwait(false);
        }
        catch (SonosControlException ex)
        {
            return MapControlException(ex, coordinator);
        }

        return await GetQueueAsync(coordinator.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads one player, optionally with live volume.
    /// </summary>
    /// <param name="id">Player id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Player info or problem.</returns>
    public async Task<ActionResult> GetPlayerAsync(string id, CancellationToken cancellationToken)
    {
        if (!_registry.TryGet(id, out var player))
        {
            return ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No player matched id");
        }

        try
        {
            var (volume, muted) = await _control.GetVolumeAsync(player, cancellationToken).ConfigureAwait(false);
            player.Volume = volume;
            player.Muted = muted;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live volume unavailable for {Player}", player.Id);
        }

        return new OkObjectResult(player.ToInfo());
    }

    /// <summary>
    /// Creates a group from player ids via LAN Control, with SOAP x-rincon fallback.
    /// </summary>
    /// <param name="request">Create body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated groups snapshot or problem.</returns>
    public async Task<ActionResult> CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken)
    {
        if (TryDisabled(out var disabled))
        {
            return disabled;
        }

        var ids = DistinctPlayerIds(request.PlayerIds);
        if (ids.Count < 2)
        {
            return ProblemResults.Create(StatusCodes.Status400BadRequest, "InvalidRequest", "playerIds must include at least two players");
        }

        if (!TryResolvePlayers(ids, out var players, out var resolveError))
        {
            return resolveError!;
        }

        var coordinatorId = string.IsNullOrWhiteSpace(request.CoordinatorId) ? ids[0] : request.CoordinatorId;
        if (!_registry.TryGetCoordinator(coordinatorId, out var coordinator))
        {
            return ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "Coordinator was not found");
        }

        var ordered = new List<string> { coordinator.Id };
        foreach (var id in ids)
        {
            if (!_registry.TryGetCoordinator(id, out var resolved))
            {
                return ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No player matched " + id);
            }

            if (!ordered.Contains(resolved.Id, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(resolved.Id);
            }
        }

        _ = players;
        await SnapshotPlaybackPositionAsync(coordinator, cancellationToken).ConfigureAwait(false);
        try
        {
            GroupCommandResult result;
            var existingGroup = coordinator.GroupId;
            var toAdd = ordered.Where(id => !string.Equals(id, coordinator.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!string.IsNullOrEmpty(existingGroup) && toAdd.Length > 0)
            {
                try
                {
                    result = await _control.ModifyGroupMembersAsync(coordinator, existingGroup, toAdd, [], cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SonosControlException)
                {
                    result = await _control.CreateGroupAsync(coordinator, ordered, existingGroup, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                result = await _control.CreateGroupAsync(coordinator, ordered, existingGroup, cancellationToken)
                    .ConfigureAwait(false);
            }

            _registry.ApplyGroupMembership(result.GroupId, result.CoordinatorId, result.PlayerIds.Count > 0 ? result.PlayerIds : ordered);
        }
        catch (SonosControlException ex) when (ex.ErrorCode is "NotSupported" or "PlayerUnavailable")
        {
            _logger.LogWarning(ex, "LAN grouping failed; using SOAP x-rincon join onto {Coordinator}", coordinator.Name);
            var soapError = await JoinViaSoapAsync(coordinator, ordered, cancellationToken).ConfigureAwait(false);
            if (soapError is not null)
            {
                return soapError;
            }

            _registry.ApplyGroupMembership(coordinator.GroupId ?? coordinator.Id, coordinator.Id, ordered);
        }
        catch (SonosControlException ex)
        {
            return MapControlException(ex, coordinator);
        }

        await ResumeQueueAfterGroupingAsync(coordinator, cancellationToken).ConfigureAwait(false);
        return GroupsSnapshot();
    }

    /// <summary>
    /// Adds or removes players on an existing group.
    /// </summary>
    /// <param name="groupId">Group id.</param>
    /// <param name="request">Add/remove body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated groups snapshot or problem.</returns>
    public async Task<ActionResult> ModifyGroupMembersAsync(
        string groupId,
        ModifyGroupMembersRequest request,
        CancellationToken cancellationToken)
    {
        if (TryDisabled(out var disabled))
        {
            return disabled;
        }

        var add = DistinctPlayerIds(request.PlayerIdsToAdd);
        var remove = DistinctPlayerIds(request.PlayerIdsToRemove);
        if (add.Count == 0 && remove.Count == 0)
        {
            return ProblemResults.Create(StatusCodes.Status400BadRequest, "InvalidRequest", "playerIdsToAdd or playerIdsToRemove is required");
        }

        if (!_registry.TryGetCoordinator(groupId, out var coordinator))
        {
            return ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No group matched id");
        }

        var resolvedAdd = new List<string>();
        foreach (var id in add)
        {
            if (!_registry.TryGetCoordinator(id, out var player))
            {
                return ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No player matched " + id);
            }

            resolvedAdd.Add(player.Id);
        }

        await SnapshotPlaybackPositionAsync(coordinator, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _control.ModifyGroupMembersAsync(
                    coordinator,
                    coordinator.GroupId ?? groupId,
                    resolvedAdd,
                    remove,
                    cancellationToken)
                .ConfigureAwait(false);
            _registry.ApplyGroupMembership(
                result.GroupId,
                result.CoordinatorId,
                result.PlayerIds.Count > 0 ? result.PlayerIds : resolvedAdd.Concat(new[] { coordinator.Id }).ToArray());
            foreach (var id in remove)
            {
                _registry.ApplyStandalone(id);
            }
        }
        catch (SonosControlException ex)
        {
            return MapControlException(ex, coordinator);
        }

        await ResumeQueueAfterGroupingAsync(coordinator, cancellationToken).ConfigureAwait(false);
        return GroupsSnapshot();
    }

    /// <summary>Current groups from the registry.</summary>
    /// <returns>Groups snapshot.</returns>
    public ActionResult GetGroups() => GroupsSnapshot();

    /// <summary>
    /// Stops plugin-owned transports. Used when the master switch is turned off.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task StopOwnedPlaybackAsync(CancellationToken cancellationToken)
    {
        foreach (var queue in _queues.Snapshot())
        {
            bool owned;
            lock (queue)
            {
                owned = queue.PluginOwned;
            }

            if (!owned)
            {
                continue;
            }

            if (!_registry.TryGet(queue.CoordinatorId, out var player)
                && !_registry.TryGetCoordinator(queue.CoordinatorId, out player))
            {
                lock (queue)
                {
                    queue.PluginOwned = false;
                    queue.State = PlaybackState.Stopped;
                }

                continue;
            }

            try
            {
                await _control.StopAsync(player, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not stop owned playback on {Player}", queue.CoordinatorId);
            }

            lock (queue)
            {
                queue.PluginOwned = false;
                queue.State = PlaybackState.Stopped;
            }
        }
    }

    private OkObjectResult GroupsSnapshot()
        => new(new GroupsResponse { Groups = _registry.GetSnapshot().Groups });

    private async Task ResumeQueueAfterGroupingAsync(DiscoveredPlayer coordinator, CancellationToken cancellationToken)
    {
        if (!_queues.TryGet(coordinator.Id, out var queue) || !LogicalQueueStore.ShouldResumeAfterGrouping(queue))
        {
            return;
        }

        if (!TryPublishedBase(out var published, out _))
        {
            return;
        }

        try
        {
            _logger.LogInformation(
                "Reloading Cloud Queue on {Player} after grouping at {PositionMs}ms",
                coordinator.Name,
                queue.PositionTicks / TimeSpan.TicksPerMillisecond);
            await StartCurrentAsync(coordinator, queue, published, queue.PositionTicks, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "First playback restore after grouping {Player} failed; retrying", coordinator.Name);
            try
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                await StartCurrentAsync(coordinator, queue, published, queue.PositionTicks, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception retryEx)
            {
                _logger.LogWarning(retryEx, "Could not restore playback after grouping {Player}", coordinator.Name);
            }
        }
    }

    private async Task SnapshotPlaybackPositionAsync(DiscoveredPlayer coordinator, CancellationToken cancellationToken)
    {
        if (!_queues.TryGet(coordinator.Id, out var queue) || queue.Items.Count == 0)
        {
            return;
        }

        try
        {
            var snap = await _control.GetTransportAsync(coordinator, cancellationToken).ConfigureAwait(false);
            if (snap.PositionTicks > 0)
            {
                queue.PositionTicks = snap.PositionTicks;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read playhead before grouping {Player}", coordinator.Name);
        }
    }

    private bool TryResolvePlayers(IReadOnlyList<string> ids, out List<DiscoveredPlayer> players, out ObjectResult? error)
    {
        players = [];
        error = null;
        foreach (var id in ids)
        {
            if (!_registry.TryGet(id, out var player) && !_registry.TryGetCoordinator(id, out player))
            {
                error = ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No player matched " + id);
                return false;
            }

            if (!player.Available)
            {
                error = ProblemResults.Create(StatusCodes.Status409Conflict, "PlayerUnavailable", player.Name + " is offline");
                return false;
            }

            players.Add(player);
        }

        return true;
    }

    private async Task<ObjectResult?> JoinViaSoapAsync(
        DiscoveredPlayer coordinator,
        IReadOnlyList<string> orderedIds,
        CancellationToken cancellationToken)
    {
        var uri = "x-rincon:" + coordinator.Id;
        foreach (var id in orderedIds)
        {
            if (string.Equals(id, coordinator.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_registry.TryGet(id, out var member))
            {
                return ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No player matched " + id);
            }

            try
            {
                await _control.SetAvTransportUriAsync(member, uri, string.Empty, cancellationToken).ConfigureAwait(false);
            }
            catch (SonosControlException ex)
            {
                return MapControlException(ex, member);
            }
        }

        return null;
    }

    private static List<string> DistinctPlayerIds(IReadOnlyList<string>? ids)
    {
        var list = new List<string>();
        if (ids is null)
        {
            return list;
        }

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!list.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(id.Trim());
            }
        }

        return list;
    }

    private async Task ApplyPlaystateAsync(
        DiscoveredPlayer coordinator,
        LogicalQueue queue,
        PlaystateRequest request,
        string published,
        CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case "Play":
                if (!queue.UsesCloudQueue && queue.Items.Count > 0)
                {
                    await StartCurrentAsync(coordinator, queue, published, 0, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _control.PlayAsync(coordinator, cancellationToken).ConfigureAwait(false);
                    lock (queue)
                    {
                        queue.State = PlaybackState.Playing;
                        queue.PluginOwned = true;
                    }
                }

                break;
            case "Pause":
                await _control.PauseAsync(coordinator, cancellationToken).ConfigureAwait(false);
                queue.State = PlaybackState.Paused;
                break;
            case "Stop":
                await _control.StopAsync(coordinator, cancellationToken).ConfigureAwait(false);
                lock (queue)
                {
                    queue.State = PlaybackState.Stopped;
                    queue.PluginOwned = false;
                }

                break;
            case "Next":
                await SkipAsync(coordinator, queue, published, +1, cancellationToken).ConfigureAwait(false);
                break;
            case "Previous":
                await SkipAsync(coordinator, queue, published, -1, cancellationToken).ConfigureAwait(false);
                break;
            case "Seek":
                var position = TimeSpan.FromTicks(request.PositionTicks ?? 0);
                await _control.SeekAsync(coordinator, position, cancellationToken).ConfigureAwait(false);
                queue.PositionTicks = position.Ticks;
                break;
            case "SetVolume":
                var volume = Math.Clamp(request.Volume ?? 0, 0, 100);
                await _control.SetVolumeAsync(coordinator, volume, cancellationToken).ConfigureAwait(false);
                queue.Volume = volume;
                coordinator.Volume = volume;
                break;
            case "Mute":
                await _control.SetMuteAsync(coordinator, true, cancellationToken).ConfigureAwait(false);
                queue.Muted = true;
                break;
            case "Unmute":
                await _control.SetMuteAsync(coordinator, false, cancellationToken).ConfigureAwait(false);
                queue.Muted = false;
                break;
            case "SetRepeat":
                queue.Repeat = string.IsNullOrEmpty(request.Repeat) ? "None" : request.Repeat;
                await _control.SetPlayModesAsync(coordinator, queue.Repeat, queue.Shuffle, queue.Crossfade, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "SetShuffle":
                LogicalQueueStore.ApplyShuffle(queue, request.Shuffle == true);
                await _control.SetPlayModesAsync(coordinator, queue.Repeat, queue.Shuffle, queue.Crossfade, cancellationToken)
                    .ConfigureAwait(false);
                if (queue.UsesCloudQueue)
                {
                    await _control.RefreshCloudQueueAsync(coordinator, cancellationToken).ConfigureAwait(false);
                }

                break;
            case "SetCrossfade":
                queue.Crossfade = request.Crossfade == true;
                await _control.SetPlayModesAsync(coordinator, queue.Repeat, queue.Shuffle, queue.Crossfade, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private async Task SkipAsync(
        DiscoveredPlayer coordinator,
        LogicalQueue queue,
        string published,
        int delta,
        CancellationToken cancellationToken)
    {
        if (queue.UsesCloudQueue)
        {
            if (delta > 0)
            {
                await _control.NextAsync(coordinator, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _control.PreviousAsync(coordinator, cancellationToken).ConfigureAwait(false);
            }

            lock (queue)
            {
                queue.CurrentIndex = Math.Clamp(queue.CurrentIndex + delta, 0, Math.Max(0, queue.Items.Count - 1));
            }

            return;
        }

        lock (queue)
        {
            queue.CurrentIndex = Math.Clamp(queue.CurrentIndex + delta, 0, Math.Max(0, queue.Items.Count - 1));
        }

        await StartCurrentAsync(coordinator, queue, published, 0, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartCurrentAsync(
        DiscoveredPlayer coordinator,
        LogicalQueue queue,
        string published,
        long startPositionTicks,
        CancellationToken cancellationToken)
    {
        LogicalQueueItem? current;
        lock (queue)
        {
            if (queue.Items.Count == 0)
            {
                return;
            }

            current = queue.Items[queue.CurrentIndex];
        }

        var queueBase = published + "/Sonos/queue/" + Uri.EscapeDataString(coordinator.Id) + "/v2.3/";
        var mediaUrl = published + "/Sonos/stream/" + current.StreamToken;
        var positionMillis = startPositionTicks > 0
            ? (int)Math.Min(startPositionTicks / TimeSpan.TicksPerMillisecond, int.MaxValue)
            : 0;
        var load = new LoadCloudQueueRequest
        {
            QueueBaseUrl = queueBase,
            ItemId = current.QueueItemId,
            QueueVersion = queue.QueueVersion,
            TrackMetadata = positionMillis > 0 ? null : CloudQueueJson.Track(published, current),
            PositionMillis = positionMillis,
            Extra = new Dictionary<string, string> { ["appContext"] = queue.UserId.ToString("N") }
        };

        try
        {
            await _control.LoadCloudQueueAsync(coordinator, load, cancellationToken).ConfigureAwait(false);
            lock (queue)
            {
                queue.UsesCloudQueue = true;
                queue.PluginOwned = true;
                queue.State = PlaybackState.Playing;
            }

            if (startPositionTicks > 0)
            {
                await SeekWhenReadyAsync(coordinator, current.QueueItemId, startPositionTicks, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SonosControlException ex) when (ex.ErrorCode is "NotSupported" or "PlayerUnavailable" or "LanAuthRequired")
        {
            _logger.LogWarning(
                ex,
                "LAN Cloud Queue unavailable for {Player} ({ErrorCode}: {Message}); using SOAP SetAVTransportURI",
                coordinator.Name,
                ex.ErrorCode,
                ex.Message);
            var didl = DidlMetadata.ForTrack(
                mediaUrl,
                current.Name,
                current.Artists.FirstOrDefault() ?? string.Empty,
                current.Album,
                current.Decision.ContentType,
                current.DurationTicks);
            await _control.SetAvTransportUriAsync(coordinator, mediaUrl, didl, cancellationToken).ConfigureAwait(false);
            await _control.PlayAsync(coordinator, cancellationToken).ConfigureAwait(false);
            lock (queue)
            {
                queue.UsesCloudQueue = false;
                queue.PluginOwned = true;
                queue.State = PlaybackState.Playing;
            }

            if (startPositionTicks > 0)
            {
                await _control.SeekAsync(coordinator, TimeSpan.FromTicks(startPositionTicks), cancellationToken).ConfigureAwait(false);
            }
        }

        queue.PositionTicks = startPositionTicks;
    }

    private async Task SeekWhenReadyAsync(
        DiscoveredPlayer coordinator,
        string itemId,
        long startPositionTicks,
        CancellationToken cancellationToken)
    {
        var targetMs = (int)Math.Min(startPositionTicks / TimeSpan.TicksPerMillisecond, int.MaxValue);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            long currentMs = 0;
            var ready = false;
            try
            {
                var snap = await _control.GetTransportAsync(coordinator, cancellationToken).ConfigureAwait(false);
                currentMs = snap.PositionTicks / TimeSpan.TicksPerMillisecond;
                ready = snap.State is PlaybackState.Playing or PlaybackState.Paused;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Waiting for seekable transport on {Player}", coordinator.Name);
            }

            if (ready && currentMs >= targetMs - 1500)
            {
                return;
            }

            if (!ready)
            {
                continue;
            }

            try
            {
                await _control.SkipToItemAsync(coordinator, itemId, targetMs, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "skipToItem at {PositionMs}ms not ready on {Player} (attempt {Attempt})", targetMs, coordinator.Name, attempt + 1);
            }
        }

        _logger.LogWarning("Could not restore playhead to {PositionMs}ms on {Player}", targetMs, coordinator.Name);
    }

    private async Task RefreshTransportIfDueAsync(DiscoveredPlayer coordinator, LogicalQueue queue, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - queue.LastPoll < PollMinInterval)
        {
            return;
        }

        try
        {
            var snap = await _control.GetTransportAsync(coordinator, cancellationToken).ConfigureAwait(false);
            LogicalQueueStore.ApplyTransport(
                queue,
                snap.State,
                snap.PositionTicks,
                snap.Volume,
                snap.Muted,
                snap.CurrentItemId,
                snap.CurrentUri);

            coordinator.Volume = snap.Volume;
            coordinator.Muted = snap.Muted;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Transport poll failed for {Player}", coordinator.Id);
            queue.LastPoll = DateTimeOffset.UtcNow;
        }
    }

    private bool TryBuildItems(
        IReadOnlyList<Guid> itemIds,
        Guid userId,
        DiscoveredPlayer coordinator,
        out List<LogicalQueueItem> items,
        out ObjectResult? error)
    {
        items = [];
        error = null;
        var probes = new List<AudioStreamInfo>(itemIds.Count);
        var loaded = new List<(MediaBrowser.Controller.Entities.BaseItem Item, AudioStreamInfo Stream)>(itemIds.Count);
        foreach (var id in itemIds)
        {
            var item = _libraryManager.GetItemById<MediaBrowser.Controller.Entities.BaseItem>(id, userId);
            if (item is null)
            {
                error = ProblemResults.Create(StatusCodes.Status404NotFound, "ItemNotFound", "Library item was not found or is not visible");
                return false;
            }

            if (item is not Audio)
            {
                error = ProblemResults.Create(StatusCodes.Status400BadRequest, "NotAudio", "Item is not audio");
                return false;
            }

            var probe = LibraryAudioProbe.FromItem(item, _mediaSources.GetMediaStreams(item.Id));
            probes.Add(probe);
            loaded.Add((item, probe));
        }

        var forcedRate = itemIds.Count > 1 ? TranscodePlanner.AlbumForcedSampleRate(probes) : null;
        var preferred = Plugin.Instance?.Configuration.PreferredTranscodeCodec ?? Configuration.TranscodeCodec.Flac;
        var expiry = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds();
        foreach (var (item, probe) in loaded)
        {
            var decision = TranscodePlanner.Plan(probe, PlayerCapabilities.S2Default, preferred, forcedRate);
            if (!decision.DirectPlay)
            {
                _logger.LogInformation("Transcode required for {Name}: {Reason}", item.Name, decision.Reason);
            }

            var token = _tokens.Mint(new StreamTokenPayload
            {
                ItemId = item.Id,
                UserId = userId,
                Container = decision.Container,
                SampleRate = decision.SampleRate,
                BitDepth = decision.BitDepth,
                DirectPlay = decision.DirectPlay,
                ExpiryUnix = expiry,
                PlayerId = coordinator.Id
            });
            var audio = item as Audio;
            var artists = audio?.Artists?.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray() ?? [];
            if (artists.Length == 0)
            {
                artists = audio?.AlbumArtists?.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray() ?? [];
            }

            items.Add(new LogicalQueueItem
            {
                ItemId = item.Id,
                Name = item.Name,
                Album = item.Album ?? string.Empty,
                Artists = artists,
                DurationTicks = item.RunTimeTicks ?? 0,
                Decision = decision,
                StreamToken = token
            });
        }

        return true;
    }

    private static bool TryPublishedBase(out string published, out ObjectResult error)
    {
        if (PublishedUrlGuard.TryValidate(Plugin.Instance?.Configuration.PublishedBaseUrl, out published, out var message))
        {
            error = null!;
            return true;
        }

        published = string.Empty;
        error = ProblemResults.Create(StatusCodes.Status400BadRequest, "PublishedUrlInvalid", message);
        return false;
    }

    private static bool TryDisabled(out ObjectResult error)
    {
        if (Plugin.Instance?.Configuration.Enabled != false)
        {
            error = null!;
            return false;
        }

        error = ProblemResults.Create(StatusCodes.Status403Forbidden, "PluginDisabled", "Sonos plugin is disabled");
        return true;
    }

    private static ObjectResult MapControlException(SonosControlException ex, DiscoveredPlayer coordinator)
    {
        var status = ex.ErrorCode switch
        {
            "LanAuthRequired" => StatusCodes.Status403Forbidden,
            "PlayerUnavailable" => StatusCodes.Status409Conflict,
            "ERROR_CLOUD_QUEUE_SERVICE_ERROR" => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status409Conflict
        };
        var details = ex.HttpStatus is int http
            ? new Dictionary<string, object?> { ["httpStatus"] = http, ["player"] = coordinator.Name }
            : new Dictionary<string, object?> { ["player"] = coordinator.Name };
        return ProblemResults.Create(status, ex.ErrorCode, ex.Message, details);
    }

    internal static QueueResponse ToResponse(LogicalQueue queue)
    {
        return new QueueResponse
        {
            CoordinatorId = queue.CoordinatorId,
            State = queue.State,
            Repeat = queue.Repeat,
            Shuffle = queue.Shuffle,
            Crossfade = queue.Crossfade,
            Volume = queue.Volume,
            Muted = queue.Muted,
            PositionTicks = queue.PositionTicks,
            CurrentIndex = queue.CurrentIndex,
            QueueVersion = queue.QueueVersion,
            UserId = queue.UserId,
            PluginOwned = queue.PluginOwned,
            Items = queue.Items.Select(i => new QueueItemDto
            {
                QueueItemId = i.QueueItemId,
                ItemId = i.ItemId,
                Name = i.Name,
                Album = i.Album,
                Artists = i.Artists,
                DurationTicks = i.DurationTicks,
                DirectPlay = i.Decision.DirectPlay,
                TranscodeReason = i.Decision.Reason == TranscodeReason.None ? null : i.Decision.Reason.ToString()
            }).ToArray()
        };
    }
}
