using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Sonos.Api;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Discovery;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class ApiContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void EmptyPlayersResponse_SerializesToSpecShape()
    {
        var json = JsonSerializer.Serialize(new PlayersResponse(), JsonOptions);

        Assert.Equal("""{"players":[],"groups":[]}""", json);
    }

    [Fact]
    public void ProblemError_OmitsDetailsWhenNull()
    {
        var result = ProblemResults.Create(
            HttpStatusCode.Conflict,
            "PlayerUnavailable",
            "Room A did not respond to loadCloudQueue");

        var body = Assert.IsType<ProblemError>(result.Value);
        var json = JsonSerializer.Serialize(body, JsonOptions);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(
            """{"error":"PlayerUnavailable","message":"Room A did not respond to loadCloudQueue"}""",
            json);
    }

    [Fact]
    public void ProblemError_IncludesDetailsWhenPresent()
    {
        var result = ProblemResults.Create(
            403,
            "LanAuthRequired",
            "Speaker returned 403",
            new Dictionary<string, object?> { ["httpStatus"] = 403 });

        var body = Assert.IsType<ProblemError>(result.Value);
        var json = JsonSerializer.Serialize(body, JsonOptions);

        Assert.Contains("\"httpStatus\":403", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QueueResponse_SerializesUserIdAndPluginOwned()
    {
        var json = JsonSerializer.Serialize(
            new QueueResponse
            {
                CoordinatorId = "RINCON_A",
                State = PlaybackState.Playing,
                UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                PluginOwned = true
            },
            JsonOptions);

        Assert.Contains("\"userId\":\"11111111-1111-1111-1111-111111111111\"", json, StringComparison.Ordinal);
        Assert.Contains("\"pluginOwned\":true", json, StringComparison.Ordinal);
    }

    private static PlayerRegistry CreateRegistry(params string[] ignoredIds)
    {
        var ignored = new HashSet<string>(ignoredIds, StringComparer.OrdinalIgnoreCase);
        return new PlayerRegistry(() => ignored);
    }

    [Fact]
    public void PlayerRegistry_StartsEmpty()
    {
        var registry = CreateRegistry();
        var snapshot = registry.GetSnapshot();

        Assert.Empty(snapshot.Players);
        Assert.Empty(snapshot.Groups);
    }

    [Fact]
    public void PlayerRegistry_ProjectsCoordinatorGroup()
    {
        var registry = CreateRegistry();
        registry.Upsert(new PlayerInfo
        {
            Id = "RINCON_A",
            Name = "Room A",
            GroupId = "RINCON_A:1",
            IsCoordinator = true,
            Available = true
        });
        registry.Upsert(new PlayerInfo
        {
            Id = "RINCON_B",
            Name = "Room B",
            GroupId = "RINCON_A:1",
            IsCoordinator = false,
            Available = true
        });

        var snapshot = registry.GetSnapshot();

        Assert.Equal(2, snapshot.Players.Count);
        var group = Assert.Single(snapshot.Groups);
        Assert.Equal("RINCON_A:1", group.Id);
        Assert.Equal("RINCON_A", group.CoordinatorId);
        Assert.Equal("Room A + Room B", group.Name);
        Assert.Equal(2, group.MemberIds.Count);
    }

    [Fact]
    public void PlayerRegistry_SkipsIgnoredIds()
    {
        var registry = CreateRegistry("RINCON_A");
        registry.Upsert(new PlayerInfo { Id = "RINCON_A", Name = "Room A" });
        registry.Upsert(new PlayerInfo { Id = "RINCON_B", Name = "Room B" });

        var snapshot = registry.GetSnapshot();

        var player = Assert.Single(snapshot.Players);
        Assert.Equal("RINCON_B", player.Id);
    }

    [Fact]
    public void GroupCommandResult_ParsesNestedGroup()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            """{"group":{"id":"G1","coordinatorId":"RINCON_A","playerIds":["RINCON_A","RINCON_B"]}}""");
        var result = Jellyfin.Plugin.Sonos.Control.GroupCommandResult.FromLan(node, "fallback", "old");

        Assert.Equal("G1", result.GroupId);
        Assert.Equal("RINCON_A", result.CoordinatorId);
        Assert.Equal(["RINCON_A", "RINCON_B"], result.PlayerIds);
    }

    [Fact]
    public void PlayerRegistry_ApplyGroupMembership_BringsFollowers()
    {
        var registry = CreateRegistry();
        registry.Upsert(new PlayerInfo { Id = "RINCON_PRIMARY", Name = "Room A", GroupId = "G-A", IsCoordinator = true });
        registry.Upsert(new PlayerInfo { Id = "RINCON_SATELLITE", Name = "Room A", GroupId = "G-A", IsCoordinator = false });
        registry.Upsert(new PlayerInfo { Id = "RINCON_OTHER", Name = "Room B", GroupId = "G-B", IsCoordinator = true });

        registry.ApplyGroupMembership("G-NEW", "RINCON_PRIMARY", ["RINCON_PRIMARY", "RINCON_OTHER"]);

        Assert.True(registry.TryGet("RINCON_SATELLITE", out var satellite));
        Assert.Equal("G-NEW", satellite.GroupId);
        Assert.False(satellite.IsCoordinator);
        Assert.True(registry.TryGet("RINCON_OTHER", out var other));
        Assert.Equal("G-NEW", other.GroupId);
        Assert.False(other.IsCoordinator);
        Assert.True(registry.TryGetCoordinator("RINCON_OTHER", out var coord));
        Assert.Equal("RINCON_PRIMARY", coord.Id);
    }

    [Fact]
    public void ParseIgnoredIds_SplitsCommaSeparated()
    {
        var ids = PlayerRegistry.ParseIgnoredIds(" RINCON_A , ,RINCON_B ");

        Assert.Equal(2, ids.Count);
        Assert.Contains("rincon_a", ids);
        Assert.Contains("RINCON_B", ids);
    }
}
