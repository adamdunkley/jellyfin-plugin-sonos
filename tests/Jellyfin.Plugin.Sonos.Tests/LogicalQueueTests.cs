using System;
using System.Linq;
using Jellyfin.Plugin.Sonos;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Queue;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class LogicalQueueTests
{
    [Fact]
    public void AddRemoveMove_AndVersionBump()
    {
        var store = new LogicalQueueStore();
        var a = Item("a");
        var b = Item("b");
        var c = Item("c");
        var queue = store.Replace("RINCON_A", [a, b], 0, Guid.NewGuid());
        var v1 = queue.QueueVersion;
        LogicalQueueStore.Add(queue, [c], next: true);
        Assert.NotEqual(v1, queue.QueueVersion);
        Assert.Equal(["a", "c", "b"], queue.Items.Select(i => i.Name).ToArray());

        var v2 = queue.QueueVersion;
        LogicalQueueStore.Move(queue, 2, 0);
        Assert.NotEqual(v2, queue.QueueVersion);
        Assert.Equal(["b", "a", "c"], queue.Items.Select(i => i.Name).ToArray());

        var v3 = queue.QueueVersion;
        LogicalQueueStore.Remove(queue, [queue.Items[1].QueueItemId]);
        Assert.NotEqual(v3, queue.QueueVersion);
        Assert.Equal(["b", "c"], queue.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void ShuffleTail_LeavesCurrentAndOnDeck()
    {
        var store = new LogicalQueueStore();
        var items = Enumerable.Range(0, 8).Select(i => Item(i.ToString())).ToArray();
        var queue = store.Replace("RINCON_A", items, 0, Guid.NewGuid());
        queue.State = PlaybackState.Playing;
        queue.CurrentIndex = 0;
        var currentId = queue.Items[0].QueueItemId;
        var onDeckId = queue.Items[1].QueueItemId;
        LogicalQueueStore.ApplyShuffle(queue, true);
        Assert.Equal(currentId, queue.Items[0].QueueItemId);
        Assert.Equal(onDeckId, queue.Items[1].QueueItemId);
        Assert.True(queue.Shuffle);
    }

    [Fact]
    public void Window_CenterAndBounds()
    {
        var items = Enumerable.Range(0, 5).Select(i => Item(i.ToString())).ToArray();
        var window = CloudQueueWindowBuilder.Slice(items, items[2].QueueItemId, 1, 1);
        Assert.Equal(3, window.Items.Count);
        Assert.False(window.IncludesBeginningOfQueue);
        Assert.False(window.IncludesEndOfQueue);

        var start = CloudQueueWindowBuilder.Slice(items, null, 0, 2);
        Assert.True(start.IncludesBeginningOfQueue);
        Assert.False(start.IncludesEndOfQueue);
        Assert.Equal(items[0].QueueItemId, start.Items[0].QueueItemId);

        var tail = CloudQueueWindowBuilder.Slice(items, items[4].QueueItemId, 10, 10);
        Assert.True(tail.IncludesBeginningOfQueue);
        Assert.True(tail.IncludesEndOfQueue);

        var empty = CloudQueueWindowBuilder.Slice([], null, 2, 2);
        Assert.True(empty.IncludesBeginningOfQueue);
        Assert.True(empty.IncludesEndOfQueue);
    }

    [Fact]
    public void CloudQueueJson_IncludesArtistAlbumArtAndVersions()
    {
        var current = new LogicalQueueItem
        {
            QueueItemId = "item-current",
            Name = "Track One",
            Album = "Example Album",
            Artists = ["Example Artist"],
            DurationTicks = 2617600000,
            StreamToken = "tok"
        };
        var next = new LogicalQueueItem
        {
            QueueItemId = "item-next",
            Name = "Track Two",
            Album = "Example Album",
            Artists = ["Example Artist"],
            StreamToken = "tok2"
        };
        var queue = new LogicalQueue { CoordinatorId = "RINCON_A", QueueVersion = "42", ContextVersion = "1" };
        queue.Items.Add(current);
        queue.Items.Add(next);

        var published = "http://192.0.2.10:8096/media";
        var track = CloudQueueJson.Track(published, current).ToJsonString();
        Assert.Contains("Example Artist", track, StringComparison.Ordinal);
        Assert.Contains("Example Album", track, StringComparison.Ordinal);
        Assert.Contains("/Sonos/image/tok", track, StringComparison.Ordinal);
        Assert.Contains("/Sonos/stream/tok", track, StringComparison.Ordinal);

        var window = CloudQueueJson.ItemWindow(
            queue,
            published,
            CloudQueueWindowBuilder.Slice(queue.Items, current.QueueItemId, 9, 10)).ToJsonString();
        Assert.Contains("\"queueVersion\":\"42\"", window, StringComparison.Ordinal);
        Assert.Contains("item-next", window, StringComparison.Ordinal);
        Assert.Contains("\"includesEndOfQueue\":true", window, StringComparison.Ordinal);

        var context = CloudQueueJson.Context(queue, published).ToJsonString();
        Assert.Contains("\"canSkip\":true", context, StringComparison.Ordinal);
        Assert.Contains("/Sonos/image/tok", context, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldResumeAfterGrouping_WhenPlayingCloudQueue()
    {
        var store = new LogicalQueueStore();
        var queue = store.Replace("RINCON_A", [Item("a")], 0, Guid.NewGuid());
        Assert.False(LogicalQueueStore.ShouldResumeAfterGrouping(queue));

        queue.UsesCloudQueue = true;
        queue.State = PlaybackState.Playing;
        Assert.True(LogicalQueueStore.ShouldResumeAfterGrouping(queue));

        queue.State = PlaybackState.Stopped;
        Assert.False(LogicalQueueStore.ShouldResumeAfterGrouping(queue));

        Assert.False(LogicalQueueStore.ShouldResumeAfterGrouping(null));
        Assert.False(LogicalQueueStore.ShouldResumeAfterGrouping(store.GetOrCreate("empty")));
    }

    [Fact]
    public void TrySyncCurrent_FollowsCloudQueueItemIdWithoutBumpingVersion()
    {
        var store = new LogicalQueueStore();
        var a = Item("Track One");
        var b = Item("Track Two");
        var queue = store.Replace("RINCON_A", [a, b], 0, Guid.NewGuid());
        var version = queue.QueueVersion;

        Assert.True(LogicalQueueStore.TrySyncCurrent(queue, b.QueueItemId, null));
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal(version, queue.QueueVersion);

        Assert.False(LogicalQueueStore.TrySyncCurrent(queue, b.QueueItemId, null));
        Assert.False(LogicalQueueStore.TrySyncCurrent(queue, "missing", null));
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void TrySyncCurrent_FollowsStreamTokenInUri()
    {
        var store = new LogicalQueueStore();
        var a = Item("a");
        a.StreamToken = "token-a";
        var b = Item("b");
        b.StreamToken = "token-b";
        var queue = store.Replace("RINCON_A", [a, b], 0, Guid.NewGuid());

        Assert.True(LogicalQueueStore.TrySyncCurrent(
            queue,
            null,
            "http://speaker.example/media/Sonos/stream/token-b"));
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void ApplyTransport_CopiesPlayheadAndCurrentRow()
    {
        var store = new LogicalQueueStore();
        var a = Item("a");
        var b = Item("b");
        var queue = store.Replace("RINCON_A", [a, b], 0, Guid.NewGuid());

        var changed = LogicalQueueStore.ApplyTransport(
            queue,
            PlaybackState.Playing,
            9_000_000,
            12,
            false,
            b.QueueItemId,
            null);

        Assert.True(changed);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal(PlaybackState.Playing, queue.State);
        Assert.Equal(9_000_000, queue.PositionTicks);
        Assert.Equal(12, queue.Volume);
    }

    [Fact]
    public void ToResponse_IncludesUserIdAndPluginOwned()
    {
        var user = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var store = new LogicalQueueStore();
        var queue = store.Replace("RINCON_A", [Item("Track One")], 0, user);
        queue.PluginOwned = true;
        queue.State = PlaybackState.Playing;

        var dto = SonosPlaybackService.ToResponse(queue);

        Assert.Equal(user, dto.UserId);
        Assert.True(dto.PluginOwned);
        Assert.Equal("RINCON_A", dto.CoordinatorId);
        Assert.Single(dto.Items);
    }

    private static LogicalQueueItem Item(string name)
        => new() { Name = name, ItemId = Guid.NewGuid() };
}
