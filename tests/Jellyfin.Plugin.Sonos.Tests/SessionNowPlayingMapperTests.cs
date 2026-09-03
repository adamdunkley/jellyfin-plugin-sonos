using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Session;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class SessionNowPlayingMapperTests
{
    private static readonly Guid TrackId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AlbumId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void FromQueueItem_FillsTitleArtistsAndAudioType()
    {
        var dto = SessionNowPlayingMapper.FromQueueItem(new QueueItemDto
        {
            ItemId = TrackId,
            Name = "Track One",
            Album = "Example Album",
            Artists = ["Example Artist"],
            DurationTicks = 9_000_000
        });

        Assert.Equal(TrackId, dto.Id);
        Assert.Equal("Track One", dto.Name);
        Assert.Equal("Example Album", dto.Album);
        Assert.Equal(["Example Artist"], dto.Artists);
        Assert.Equal(9_000_000, dto.RunTimeTicks);
        Assert.Equal(MediaType.Audio, dto.MediaType);
        Assert.Equal(BaseItemKind.Audio, dto.Type);
    }

    [Fact]
    public void FromQueueItem_KeepsAlbumArtworkFieldsTheNowPlayingBarReads()
    {
        var dto = SessionNowPlayingMapper.FromQueueItem(new QueueItemDto
        {
            ItemId = TrackId,
            Name = "Track Two",
            Album = "Example Album",
            Artists = ["Example Artist"],
            DurationTicks = 1
        });

        dto.AlbumId = AlbumId;
        dto.AlbumPrimaryImageTag = "album-tag";
        dto.ImageTags = new Dictionary<ImageType, string>
        {
            [ImageType.Primary] = "track-tag"
        };

        Assert.Equal("Track Two", dto.Name);
        Assert.Equal(AlbumId, dto.AlbumId);
        Assert.Equal("album-tag", dto.AlbumPrimaryImageTag);
        Assert.Equal("track-tag", dto.ImageTags[ImageType.Primary]);
    }

    [Fact]
    public void ShouldStart_WhenLibraryItemOrQueueRowChanges()
    {
        var first = new QueueItemDto { ItemId = TrackId, QueueItemId = "q1", Name = "Track One" };
        var next = new QueueItemDto { ItemId = AlbumId, QueueItemId = "q2", Name = "Track Two" };
        var again = new QueueItemDto { ItemId = TrackId, QueueItemId = "q3", Name = "Track One" };

        Assert.True(SessionPlaybackReporter.ShouldStart(first, Guid.Empty, string.Empty, PlaybackState.Stopped));
        Assert.False(SessionPlaybackReporter.ShouldStart(first, TrackId, "q1", PlaybackState.Playing));
        Assert.True(SessionPlaybackReporter.ShouldStart(next, TrackId, "q1", PlaybackState.Playing));
        Assert.True(SessionPlaybackReporter.ShouldStart(again, TrackId, "q1", PlaybackState.Playing));
    }
}
