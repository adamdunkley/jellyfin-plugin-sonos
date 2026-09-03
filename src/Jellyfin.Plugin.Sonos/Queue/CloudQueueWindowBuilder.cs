using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Sonos.Queue;

/// <summary>
/// Builds Sonos Cloud Queue itemWindow slices.
/// </summary>
public static class CloudQueueWindowBuilder
{
    /// <summary>
    /// Slices a logical queue around an item id.
    /// </summary>
    /// <param name="items">Full logical queue.</param>
    /// <param name="itemId">Center item id, or empty for the start.</param>
    /// <param name="previousWindowSize">Items before center.</param>
    /// <param name="upcomingWindowSize">Items after center.</param>
    /// <returns>The window.</returns>
    public static CloudQueueWindow Slice(
        IReadOnlyList<LogicalQueueItem> items,
        string? itemId,
        int previousWindowSize,
        int upcomingWindowSize)
    {
        previousWindowSize = Math.Max(0, previousWindowSize);
        upcomingWindowSize = Math.Max(0, upcomingWindowSize);
        if (items.Count == 0)
        {
            return new CloudQueueWindow
            {
                Items = [],
                IncludesBeginningOfQueue = true,
                IncludesEndOfQueue = true
            };
        }

        var center = 0;
        if (!string.IsNullOrEmpty(itemId))
        {
            var found = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].QueueItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                center = found;
            }
        }

        var start = Math.Max(0, center - previousWindowSize);
        var end = Math.Min(items.Count - 1, center + upcomingWindowSize);
        var slice = items.Skip(start).Take(end - start + 1).ToArray();
        return new CloudQueueWindow
        {
            Items = slice,
            IncludesBeginningOfQueue = start == 0,
            IncludesEndOfQueue = end == items.Count - 1
        };
    }
}
