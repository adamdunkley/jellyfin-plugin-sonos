using System;
using System.IO;
using Jellyfin.Plugin.Sonos.Discovery;
using Jellyfin.Plugin.Sonos.Util;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void DeviceDescriptionParser_ReadsRequiredFields()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "device_description.xml"));
        var desc = DeviceDescriptionParser.Parse(xml);

        Assert.NotNull(desc);
        Assert.Equal("RINCON_TESTPLAYER1", desc.Id);
        Assert.Equal("Room A", desc.RoomName);
        Assert.Equal("TEST", desc.ModelNumber);
        Assert.Equal("Sonos Test Speaker", desc.ModelName);
        Assert.Equal("Test Speaker", desc.DisplayName);
    }

    [Fact]
    public void ZoneGroupStateParser_FiltersS1AndInvisible()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zone_group_state.xml"));
        var members = ZoneGroupStateParser.Parse(xml);

        Assert.Equal(3, members.Count);
        var roomA = Assert.Single(members, m => m.Id == "RINCON_A");
        Assert.True(ZoneGroupStateParser.IsS2(roomA));
        Assert.Equal("Room A", roomA.ZoneName);
        Assert.Equal("RINCON_A", roomA.CoordinatorId);

        var s1 = Assert.Single(members, m => m.Id == "RINCON_S1");
        Assert.False(ZoneGroupStateParser.IsS2(s1));

        var satellite = Assert.Single(members, m => m.Id == "RINCON_SAT");
        Assert.True(satellite.Invisible);
    }

    [Fact]
    public void IpListParser_IgnoresJunkAndDedupes()
    {
        var ips = IpListParser.Parse(" 192.0.2.20, not-an-ip, 192.0.2.20, 192.0.2.21 ");

        Assert.Equal(["192.0.2.20", "192.0.2.21"], ips);
    }

    [Fact]
    public void PlayerRegistry_OverwritesIpOnSameRincon()
    {
        var registry = new PlayerRegistry(() => new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase));
        registry.Upsert(new DiscoveredPlayer { Id = "RINCON_A", Name = "Room A", Ip = "192.0.2.20", IsCoordinator = true, GroupId = "G1" });
        registry.Upsert(new DiscoveredPlayer { Id = "RINCON_A", Name = "Room A", Ip = "192.0.2.99", IsCoordinator = true, GroupId = "G1" });

        Assert.True(registry.TryGet("RINCON_A", out var player));
        Assert.Equal("192.0.2.99", player.Ip);
        Assert.True(player.Available);
    }

    [Fact]
    public void PlayerRegistry_MarksStaleAfterThreeMisses()
    {
        var registry = new PlayerRegistry(() => new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase));
        registry.Upsert(new DiscoveredPlayer { Id = "RINCON_A", Name = "Room A" });
        registry.CompleteCycle([]);
        registry.CompleteCycle([]);
        Assert.True(registry.TryGet("RINCON_A", out var afterTwo));
        Assert.True(afterTwo.Available);
        registry.CompleteCycle([]);
        Assert.True(registry.TryGet("RINCON_A", out var afterThree));
        Assert.False(afterThree.Available);
    }
}
