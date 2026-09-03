using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Sonos.Api.Models;

namespace Jellyfin.Plugin.Sonos.Queue;

/// <summary>
/// Logical queues keyed by coordinator id.
/// </summary>
public sealed class LogicalQueueStore
{
    private readonly ConcurrentDictionary<string, LogicalQueue> _queues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or creates a queue for a coordinator.
    /// </summary>
    /// <param name="coordinatorId">Coordinator id.</param>
    /// <returns>The queue.</returns>
    public LogicalQueue GetOrCreate(string coordinatorId)
    {
        return _queues.GetOrAdd(coordinatorId, id => new LogicalQueue { CoordinatorId = id });
    }

    /// <summary>
    /// Tries to get a queue.
    /// </summary>
    /// <param name="coordinatorId">Coordinator id.</param>
    /// <param name="queue">The queue.</param>
    /// <returns>True if present.</returns>
    public bool TryGet(string coordinatorId, out LogicalQueue queue)
        => _queues.TryGetValue(coordinatorId, out queue!);

    /// <summary>
    /// Returns a snapshot of all coordinator queues.
    /// </summary>
    /// <returns>Queues currently in the store.</returns>
    public IReadOnlyList<LogicalQueue> Snapshot()
        => _queues.Values.ToArray();

    /// <summary>
    /// Replaces the queue contents.
    /// </summary>
    /// <param name="coordinatorId">Coordinator id.</param>
    /// <param name="items">New items.</param>
    /// <param name="startIndex">Start index.</param>
    /// <param name="userId">Calling user.</param>
    /// <returns>The queue.</returns>
    public LogicalQueue Replace(string coordinatorId, IReadOnlyList<LogicalQueueItem> items, int startIndex, Guid userId)
    {
        var queue = GetOrCreate(coordinatorId);
        lock (queue)
        {
            queue.Items.Clear();
            queue.Items.AddRange(items);
            queue.CurrentIndex = Math.Clamp(startIndex, 0, Math.Max(0, queue.Items.Count - 1));
            queue.UserId = userId;
            queue.BumpVersion();
            return queue;
        }
    }

    /// <summary>
    /// Adds items Next or Last.
    /// </summary>
    /// <param name="queue">Queue.</param>
    /// <param name="items">Items to add.</param>
    /// <param name="next">True = after current, false = end.</param>
    public static void Add(LogicalQueue queue, IReadOnlyList<LogicalQueueItem> items, bool next)
    {
        lock (queue)
        {
            if (next && queue.Items.Count > 0)
            {
                queue.Items.InsertRange(queue.CurrentIndex + 1, items);
            }
            else
            {
                queue.Items.AddRange(items);
            }

            queue.BumpVersion();
        }
    }

    /// <summary>
    /// Removes items by queue item id.
    /// </summary>
    /// <param name="queue">Queue.</param>
    /// <param name="queueItemIds">Ids to remove.</param>
    public static void Remove(LogicalQueue queue, IReadOnlyList<string> queueItemIds)
    {
        lock (queue)
        {
            var set = new HashSet<string>(queueItemIds, StringComparer.OrdinalIgnoreCase);
            var currentId = queue.CurrentIndex >= 0 && queue.CurrentIndex < queue.Items.Count
                ? queue.Items[queue.CurrentIndex].QueueItemId
                : null;
            queue.Items.RemoveAll(i => set.Contains(i.QueueItemId));
            if (currentId is not null)
            {
                var idx = queue.Items.FindIndex(i => string.Equals(i.QueueItemId, currentId, StringComparison.OrdinalIgnoreCase));
                queue.CurrentIndex = idx >= 0 ? idx : Math.Min(queue.CurrentIndex, Math.Max(0, queue.Items.Count - 1));
            }

            queue.BumpVersion();
        }
    }

    /// <summary>
    /// Moves an item.
    /// </summary>
    /// <param name="queue">Queue.</param>
    /// <param name="fromIndex">From.</param>
    /// <param name="toIndex">To.</param>
    public static void Move(LogicalQueue queue, int fromIndex, int toIndex)
    {
        lock (queue)
        {
            if (fromIndex < 0 || fromIndex >= queue.Items.Count || toIndex < 0 || toIndex >= queue.Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            }

            var item = queue.Items[fromIndex];
            queue.Items.RemoveAt(fromIndex);
            queue.Items.Insert(toIndex, item);
            if (queue.CurrentIndex == fromIndex)
            {
                queue.CurrentIndex = toIndex;
            }
            else if (fromIndex < queue.CurrentIndex && toIndex >= queue.CurrentIndex)
            {
                queue.CurrentIndex--;
            }
            else if (fromIndex > queue.CurrentIndex && toIndex <= queue.CurrentIndex)
            {
                queue.CurrentIndex++;
            }

            queue.BumpVersion();
        }
    }

    /// <summary>
    /// True when grouping should reload Cloud Queue so the speaker keeps playing.
    /// </summary>
    /// <param name="queue">Coordinator queue, if any.</param>
    /// <returns>True when the speaker had a loaded or playing queue.</returns>
    public static bool ShouldResumeAfterGrouping(LogicalQueue? queue)
    {
        if (queue is null || queue.Items.Count == 0)
        {
            return false;
        }

        return queue.State is Api.Models.PlaybackState.Playing or Api.Models.PlaybackState.Transitioning
            || (queue.UsesCloudQueue && queue.State != Api.Models.PlaybackState.Stopped);
    }

    /// <summary>
    /// Shuffles the tail only (not current or on-deck) when already playing.
    /// </summary>
    /// <param name="queue">Queue.</param>
    /// <param name="shuffle">Whether shuffle is on.</param>
    public static void ApplyShuffle(LogicalQueue queue, bool shuffle)
    {
        lock (queue)
        {
            queue.Shuffle = shuffle;
            if (!shuffle || queue.Items.Count < 3)
            {
                queue.BumpVersion();
                return;
            }

            var start = queue.State is Api.Models.PlaybackState.Playing or Api.Models.PlaybackState.Paused
                ? Math.Min(queue.CurrentIndex + 2, queue.Items.Count)
                : 0;
            if (start >= queue.Items.Count - 1)
            {
                queue.BumpVersion();
                return;
            }

            var tail = queue.Items.Skip(start).ToList();
            var rng = new Random();
            for (var i = tail.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (tail[i], tail[j]) = (tail[j], tail[i]);
            }

            queue.Items.RemoveRange(start, queue.Items.Count - start);
            queue.Items.AddRange(tail);
            queue.BumpVersion();
        }
    }

    /// <summary>
    /// Points <see cref="LogicalQueue.CurrentIndex"/> at the item the speaker is playing.
    /// Does not bump queueVersion: playhead is not a queue rewrite.
    /// </summary>
    /// <param name="queue">Queue.</param>
    /// <param name="itemId">Cloud Queue item id from playbackStatus, if any.</param>
    /// <param name="currentUri">SOAP TrackURI / stream URL, if any.</param>
    /// <returns>True when the playing row changed.</returns>
    public static bool TrySyncCurrent(LogicalQueue queue, string? itemId, string? currentUri)
    {
        ArgumentNullException.ThrowIfNull(queue);
        lock (queue)
        {
            return SyncCurrentUnlocked(queue, itemId, currentUri);
        }
    }

    /// <summary>
    /// Applies a transport snapshot, including the current Cloud Queue row.
    /// </summary>
    /// <param name="queue">Queue.</param>
    /// <param name="state">Playback state.</param>
    /// <param name="positionTicks">Playhead.</param>
    /// <param name="volume">Volume 0-100.</param>
    /// <param name="muted">Mute.</param>
    /// <param name="itemId">Cloud Queue item id, if any.</param>
    /// <param name="currentUri">Stream URI, if any.</param>
    /// <returns>True when the playing row changed.</returns>
    public static bool ApplyTransport(
        LogicalQueue queue,
        PlaybackState state,
        long positionTicks,
        int volume,
        bool muted,
        string? itemId,
        string? currentUri)
    {
        ArgumentNullException.ThrowIfNull(queue);
        lock (queue)
        {
            queue.State = state;
            queue.PositionTicks = positionTicks;
            queue.Volume = volume;
            queue.Muted = muted;
            var changed = SyncCurrentUnlocked(queue, itemId, currentUri);
            queue.LastPoll = DateTimeOffset.UtcNow;
            return changed;
        }
    }

    internal static bool SyncCurrentUnlocked(LogicalQueue queue, string? itemId, string? currentUri)
    {
        if (queue.Items.Count == 0)
        {
            return false;
        }

        var index = IndexOfCurrent(queue, itemId, currentUri);
        if (index < 0 || index == queue.CurrentIndex)
        {
            return false;
        }

        queue.CurrentIndex = index;
        return true;
    }

    private static int IndexOfCurrent(LogicalQueue queue, string? itemId, string? currentUri)
    {
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            for (var i = 0; i < queue.Items.Count; i++)
            {
                if (string.Equals(queue.Items[i].QueueItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(currentUri))
        {
            return -1;
        }

        for (var i = 0; i < queue.Items.Count; i++)
        {
            var token = queue.Items[i].StreamToken;
            if (!string.IsNullOrEmpty(token)
                && currentUri.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
