using System;
using Jellyfin.Plugin.Sonos.Streaming;

namespace Jellyfin.Plugin.Sonos.Queue;

/// <summary>
/// One item in the plugin-owned logical queue.
/// </summary>
public sealed class LogicalQueueItem
{
    /// <summary>Gets the immutable Sonos item id.</summary>
    public string QueueItemId { get; init; } = Guid.NewGuid().ToString("N");

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

    /// <summary>Gets the transcode plan.</summary>
    public TranscodeDecision Decision { get; init; } = new() { DirectPlay = true };

    /// <summary>Gets the minted stream token.</summary>
    public string StreamToken { get; set; } = string.Empty;
}
