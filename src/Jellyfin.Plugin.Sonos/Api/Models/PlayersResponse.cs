using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// Response for <c>GET /Sonos/Players</c>.
/// </summary>
public sealed class PlayersResponse
{
    /// <summary>
    /// Gets the discovered players.
    /// </summary>
    public IReadOnlyList<PlayerInfo> Players { get; init; } = [];

    /// <summary>
    /// Gets the current Sonos groups.
    /// </summary>
    public IReadOnlyList<GroupInfo> Groups { get; init; } = [];
}
