using System.Linq;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.Sonos.Queue;

/// <summary>
/// Cloud Queue JSON payloads matching the Sonos itemWindow / context / playback-object shape.
/// </summary>
public static class CloudQueueJson
{
    /// <summary>Builds a playback-object track (loadCloudQueue trackMetadata and itemWindow.track).</summary>
    /// <param name="published">Published base URL.</param>
    /// <param name="item">Logical queue item.</param>
    /// <returns>Track object.</returns>
    public static JsonObject Track(string published, LogicalQueueItem item)
    {
        var track = new JsonObject
        {
            ["type"] = "track",
            ["mediaUrl"] = published + "/Sonos/stream/" + item.StreamToken,
            ["contentType"] = item.Decision.ContentType,
            ["durationMillis"] = item.DurationTicks / System.TimeSpan.TicksPerMillisecond,
            ["name"] = item.Name,
            ["imageUrl"] = published + "/Sonos/image/" + item.StreamToken,
            ["service"] = new JsonObject { ["name"] = "Jellyfin", ["id"] = "jellyfin" }
        };

        var artist = item.Artists.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        if (!string.IsNullOrEmpty(artist))
        {
            track["artist"] = new JsonObject { ["name"] = artist };
        }

        if (!string.IsNullOrWhiteSpace(item.Album))
        {
            var album = new JsonObject { ["name"] = item.Album };
            if (!string.IsNullOrEmpty(artist))
            {
                album["artist"] = new JsonObject { ["name"] = artist };
            }

            track["album"] = album;
        }

        return track;
    }

    /// <summary>Builds one itemWindow item.</summary>
    /// <param name="published">Published base URL.</param>
    /// <param name="item">Logical queue item.</param>
    /// <param name="crossfade">Whether crossfade is allowed.</param>
    /// <param name="canSkip">Whether skip is allowed from this item.</param>
    /// <returns>Queue item object.</returns>
    public static JsonObject Item(string published, LogicalQueueItem item, bool crossfade, bool canSkip)
    {
        return new JsonObject
        {
            ["id"] = item.QueueItemId,
            ["deleted"] = false,
            ["policies"] = new JsonObject
            {
                ["canSkip"] = canSkip,
                ["canSkipToItem"] = canSkip,
                ["canSkipBack"] = true,
                ["canCrossfade"] = crossfade
            },
            ["track"] = Track(published, item)
        };
    }

    /// <summary>Builds GET /context.</summary>
    /// <param name="queue">Logical queue.</param>
    /// <param name="published">Published base URL.</param>
    /// <returns>Context object.</returns>
    public static JsonObject Context(LogicalQueue queue, string published)
    {
        var imageUrl = queue.Items.Count > 0
            ? published + "/Sonos/image/" + queue.Items[0].StreamToken
            : string.Empty;
        var container = new JsonObject
        {
            ["name"] = string.IsNullOrEmpty(queue.ContainerName) ? "Jellyfin" : queue.ContainerName,
            ["type"] = "trackList",
            ["id"] = queue.CoordinatorId,
            ["service"] = new JsonObject { ["name"] = "Jellyfin", ["id"] = "jellyfin" }
        };
        if (!string.IsNullOrEmpty(imageUrl))
        {
            container["imageUrl"] = imageUrl;
        }

        return new JsonObject
        {
            ["container"] = container,
            ["playbackPolicies"] = Policies(queue.Crossfade),
            ["reports"] = new JsonObject
            {
                ["sendUpdateAfterMillis"] = 0,
                ["periodicIntervalMillis"] = 0,
                ["sendPlaybackActions"] = false
            },
            ["contextVersion"] = queue.ContextVersion,
            ["queueVersion"] = queue.QueueVersion
        };
    }

    /// <summary>Builds GET /itemWindow.</summary>
    /// <param name="queue">Logical queue.</param>
    /// <param name="published">Published base URL.</param>
    /// <param name="window">Sliced window.</param>
    /// <returns>Window object.</returns>
    public static JsonObject ItemWindow(LogicalQueue queue, string published, CloudQueueWindow window)
    {
        var lastId = queue.Items.Count > 0 ? queue.Items[^1].QueueItemId : null;
        var items = new JsonArray();
        foreach (var item in window.Items)
        {
            var canSkip = !string.Equals(item.QueueItemId, lastId, System.StringComparison.Ordinal);
            items.Add(Item(published, item, queue.Crossfade, canSkip));
        }

        return new JsonObject
        {
            ["items"] = items,
            ["includesBeginningOfQueue"] = window.IncludesBeginningOfQueue,
            ["includesEndOfQueue"] = window.IncludesEndOfQueue,
            ["contextVersion"] = queue.ContextVersion,
            ["queueVersion"] = queue.QueueVersion
        };
    }

    private static JsonObject Policies(bool crossfade)
    {
        return new JsonObject
        {
            ["canSkip"] = true,
            ["canSkipToItem"] = true,
            ["canSkipBack"] = true,
            ["limitedSkips"] = false,
            ["canSeek"] = true,
            ["canPause"] = true,
            ["canStop"] = true,
            ["canRepeat"] = true,
            ["canRepeatOne"] = true,
            ["canCrossfade"] = crossfade,
            ["canShuffle"] = true,
            ["showNNextTracks"] = 10,
            ["showNPreviousTracks"] = 10
        };
    }
}
