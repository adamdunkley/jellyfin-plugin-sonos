using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Queue/Remove</c> body.
/// </summary>
public sealed class RemoveQueueRequest
{
    /// <summary>Gets the player or group id.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Gets logical queue item ids to remove.</summary>
    public IReadOnlyList<string> QueueItemIds { get; init; } = [];
}
