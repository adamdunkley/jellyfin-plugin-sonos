using System;
using Jellyfin.Plugin.Sonos;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Control;
using Jellyfin.Plugin.Sonos.Discovery;
using Jellyfin.Plugin.Sonos.Queue;
using Jellyfin.Plugin.Sonos.Session;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

/// <summary>
/// Contract tests for Queue/Play Cloud Queue load and missing-session mapping.
/// </summary>
public sealed class QueuePlaySessionTests
{
    [Fact]
    public void LoadCloudQueue_ZeroOffset_SendsTrackMetadata()
    {
        var load = BuildLoad(startPositionTicks: 0);

        Assert.NotNull(load.TrackMetadata);
        Assert.Equal(0, load.PositionMillis);
        Assert.Equal("item-current", load.ItemId);
    }

    [Fact]
    public void LoadCloudQueue_ResumeOffset_StillSendsTrackMetadata()
    {
        var ticks = TimeSpan.FromSeconds(3.6).Ticks;
        var load = BuildLoad(ticks);

        Assert.NotNull(load.TrackMetadata);
        Assert.Equal(3600, load.PositionMillis);
        Assert.Contains("tok", load.TrackMetadata!["mediaUrl"]?.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void MapControlException_MissingSession_DoesNotLeakFirmwareCode()
    {
        var coordinator = new DiscoveredPlayer { Id = "RINCON_LIVING", Name = "Living Room" };
        var ex = new SonosControlException(
            "ERROR_INVALID_OBJECT_ID",
            "There is no session on this player.");

        var result = SonosPlaybackService.MapControlException(ex, coordinator);
        var body = Assert.IsType<ProblemError>(result.Value);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("PlayerUnavailable", body.Error);
        Assert.DoesNotContain("ERROR_INVALID_OBJECT_ID", body.Error, StringComparison.Ordinal);
        Assert.Contains("Living Room", body.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapControlException_MissingSession_IsNotSuccessForClients()
    {
        var coordinator = new DiscoveredPlayer { Id = "RINCON_LIVING", Name = "Living Room" };
        var ex = new SonosControlException(
            "ERROR_INVALID_OBJECT_ID",
            "There is no session on this player.");

        var result = SonosPlaybackService.MapControlException(ex, coordinator);

        Assert.False(ActionResultReader.IsSuccess(result));
        Assert.NotEqual("ERROR_INVALID_OBJECT_ID", ActionResultReader.ErrorCode(result));
    }

    [Theory]
    [InlineData("ERROR_INVALID_OBJECT_ID", "There is no session on this player.", true)]
    [InlineData("error_invalid_object_id", "There is no session on this player.", true)]
    [InlineData("sessionError", "There is no session on this player.", true)]
    [InlineData("playbackError", "no session", true)]
    [InlineData("LanAuthRequired", "Speaker returned 403", false)]
    [InlineData("ERROR_CLOUD_QUEUE_SERVICE_ERROR", "cloud queue failed", false)]
    [InlineData("PlayerUnavailable", "loadCloudQueue timed out", false)]
    public void IsMissingPlaybackSession_DetectsFirmwareNoSession(string errorCode, string message, bool expected)
    {
        var ex = new SonosControlException(errorCode, message);

        Assert.Equal(expected, ex.IsMissingPlaybackSession());
    }

    private static LoadCloudQueueRequest BuildLoad(long startPositionTicks)
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
        var queue = new LogicalQueue
        {
            CoordinatorId = "RINCON_LIVING",
            QueueVersion = "42",
            UserId = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };
        queue.Items.Add(current);

        return SonosPlaybackService.BuildLoadCloudQueueRequest(
            "RINCON_LIVING",
            queue,
            current,
            "http://192.0.2.10:8096",
            startPositionTicks);
    }
}
