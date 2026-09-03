using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Queue/Add</c> body.
/// </summary>
public sealed class AddQueueRequest
{
    /// <summary>Gets the player or group id.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Gets library item ids to enqueue.</summary>
    public IReadOnlyList<Guid> ItemIds { get; init; } = [];

    /// <summary>Gets Next or Last.</summary>
    public string Mode { get; init; } = "Last";
}
