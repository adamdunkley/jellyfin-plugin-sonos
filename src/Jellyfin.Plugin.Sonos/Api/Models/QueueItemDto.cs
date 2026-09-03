using System;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// One logical queue item as seen by clients.
/// </summary>
public sealed class QueueItemDto
{
    /// <summary>Gets the immutable queue item id.</summary>
    public string QueueItemId { get; init; } = string.Empty;

    /// <summary>Gets the Jellyfin item id.</summary>
    public Guid ItemId { get; init; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the album.</summary>
    public string Album { get; init; } = string.Empty;

    /// <summary>Gets artists.</summary>
    public string[] Artists { get; init; } = [];

    /// <summary>Gets duration in ticks.</summary>
    public long DurationTicks { get; init; }

    /// <summary>Gets a value indicating whether the file is sent unmodified.</summary>
    public bool DirectPlay { get; init; }

    /// <summary>Gets the transcode reason when not direct-play.</summary>
    public string? TranscodeReason { get; init; }
}
