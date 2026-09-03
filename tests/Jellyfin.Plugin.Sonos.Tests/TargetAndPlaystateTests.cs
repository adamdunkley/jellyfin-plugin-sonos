using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Control;
using Jellyfin.Plugin.Sonos.Discovery;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class TargetAndPlaystateTests
{
    [Fact]
    public void TargetResolver_PlayerMapsToCoordinator()
    {
        var registry = new PlayerRegistry(() => new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase));
        registry.Upsert(new DiscoveredPlayer
        {
            Id = "RINCON_A",
            Name = "Room A",
            GroupId = "G1",
            IsCoordinator = true,
            Available = true
        });
        registry.Upsert(new DiscoveredPlayer
        {
            Id = "RINCON_B",
            Name = "Room B",
            GroupId = "G1",
            IsCoordinator = false,
            Available = true
        });
        var resolver = new TargetResolver(registry);
        Assert.True(resolver.TryResolve("RINCON_B", out var coordinator, out var error));
        Assert.Null(error);
        Assert.Equal("RINCON_A", coordinator.Id);
    }

    [Fact]
    public void TargetResolver_MissingIs404()
    {
        var registry = new PlayerRegistry(() => new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase));
        var resolver = new TargetResolver(registry);
        Assert.False(resolver.TryResolve("nope", out _, out var error));
        Assert.Equal(404, error!.StatusCode);
    }

    [Fact]
    public void TargetResolver_OfflineIs409()
    {
        var registry = new PlayerRegistry(() => new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase));
        registry.Upsert(new DiscoveredPlayer { Id = "RINCON_A", Name = "Room A", IsCoordinator = true, Available = true });
        registry.CompleteCycle([]);
        registry.CompleteCycle([]);
        registry.CompleteCycle([]);
        var resolver = new TargetResolver(registry);
        Assert.False(resolver.TryResolve("RINCON_A", out _, out var error));
        Assert.Equal(409, error!.StatusCode);
    }

    [Fact]
    public void PlaystateCommands_UnknownIsRejected()
    {
        Assert.False(PlaystateCommands.IsKnown("Explode"));
        Assert.False(PlaystateCommands.IsKnown(null));
        Assert.True(PlaystateCommands.IsKnown("Play"));
        Assert.True(PlaystateCommands.IsKnown("SetCrossfade"));
    }

    [Fact]
    public void PlayQueueRequest_EmptyItemIdsIsInvalidByConvention()
    {
        var request = new PlayQueueRequest { TargetId = "RINCON_A", ItemIds = [] };
        Assert.Empty(request.ItemIds);
        Assert.False(string.IsNullOrEmpty(request.TargetId));
    }

    [Fact]
    public void TransportSnapshot_ReadsIntAndStringMillis()
    {
        var fromInt = System.Text.Json.Nodes.JsonNode.Parse("""{"positionMillis":1500}""");
        Assert.Equal(1500 * System.TimeSpan.TicksPerMillisecond, TransportSnapshot.PositionTicksFromStatus(fromInt));

        var fromString = System.Text.Json.Nodes.JsonNode.Parse("""{"positionMillis":"2500"}""");
        Assert.Equal(2500 * System.TimeSpan.TicksPerMillisecond, TransportSnapshot.PositionTicksFromStatus(fromString));

        Assert.Equal(0, TransportSnapshot.PositionTicksFromStatus(null));
    }

    [Fact]
    public void TransportSnapshot_ReadsCloudQueueItemId()
    {
        var fromTop = System.Text.Json.Nodes.JsonNode.Parse("""{"itemId":"item-next","positionMillis":12}""");
        Assert.Equal("item-next", TransportSnapshot.ItemIdFromStatus(fromTop));

        var fromNested = System.Text.Json.Nodes.JsonNode.Parse("""{"currentItem":{"itemId":"nested-id"}}""");
        Assert.Equal("nested-id", TransportSnapshot.ItemIdFromStatus(fromNested));

        Assert.Null(TransportSnapshot.ItemIdFromStatus(null));
        Assert.Null(TransportSnapshot.ItemIdFromStatus(System.Text.Json.Nodes.JsonNode.Parse("""{"positionMillis":1}""")));
    }

    [Fact]
    public void TransportSnapshot_ReadsGroupVolumeAndMute()
    {
        var parsed = TransportSnapshot.VolumeFromStatus(
            System.Text.Json.Nodes.JsonNode.Parse("""{"volume":18,"muted":true}"""));
        Assert.Equal(18, parsed.Volume);
        Assert.True(parsed.Muted);

        var fromString = TransportSnapshot.VolumeFromStatus(
            System.Text.Json.Nodes.JsonNode.Parse("""{"volume":"40","muted":"false"}"""));
        Assert.Equal(40, fromString.Volume);
        Assert.False(fromString.Muted);

        Assert.Equal(0, TransportSnapshot.VolumeFromStatus(null).Volume);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("None", false, false)]
    [InlineData("All", true, false)]
    [InlineData("One", false, true)]
    [InlineData("one", false, true)]
    public void PlayModeMapper_ToLanFlags(string? repeat, bool expectedAll, bool expectedOne)
    {
        PlayModeMapper.ToLanFlags(repeat, out var repeatAll, out var repeatOne);

        Assert.Equal(expectedAll, repeatAll);
        Assert.Equal(expectedOne, repeatOne);
    }

    [Theory]
    [InlineData("None", false, "NORMAL")]
    [InlineData("All", false, "REPEAT_ALL")]
    [InlineData("One", false, "REPEAT_ONE")]
    [InlineData("None", true, "SHUFFLE_NOREPEAT")]
    [InlineData("All", true, "SHUFFLE")]
    [InlineData("One", true, "SHUFFLE_REPEAT_ONE")]
    public void PlayModeMapper_ToSoapPlayMode(string repeat, bool shuffle, string expected)
    {
        Assert.Equal(expected, PlayModeMapper.ToSoapPlayMode(repeat, shuffle));
    }
}
