using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Queue/Play</c> body.
/// </summary>
public sealed class PlayQueueRequest
{
    /// <summary>Gets the player or group id.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Gets library item ids to play.</summary>
    public IReadOnlyList<Guid> ItemIds { get; init; } = [];

    /// <summary>Gets the start index in <see cref="ItemIds"/>.</summary>
    public int StartIndex { get; init; }

    /// <summary>Gets the start position in ticks.</summary>
    public long StartPositionTicks { get; init; }
}
