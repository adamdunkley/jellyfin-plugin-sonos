using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Parameters for loadCloudQueue.
/// </summary>
public sealed class LoadCloudQueueRequest
{
    /// <summary>Gets the queue base URL ending with /v2.3/.</summary>
    public string QueueBaseUrl { get; init; } = string.Empty;

    /// <summary>Gets the first item id.</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Gets the queue version.</summary>
    public string QueueVersion { get; init; } = string.Empty;

    /// <summary>Gets the media URL of the first track.</summary>
    public string? FirstMediaUrl { get; init; }

    /// <summary>Gets the first track title.</summary>
    public string? FirstTrackName { get; init; }

    /// <summary>Gets optional HTTP authorization the speaker should attach.</summary>
    public string? HttpAuthorization { get; init; }

    /// <summary>Gets full trackMetadata for the first item (artist, album, art, duration).</summary>
    public System.Text.Json.Nodes.JsonObject? TrackMetadata { get; init; }

    /// <summary>Gets extra metadata fields.</summary>
    public IReadOnlyDictionary<string, string>? Extra { get; init; }

    /// <summary>Gets start offset within the first item, in milliseconds.</summary>
    public int PositionMillis { get; init; }
}
