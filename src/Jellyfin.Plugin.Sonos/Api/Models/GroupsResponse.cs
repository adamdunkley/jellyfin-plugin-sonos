using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// Response for <c>GET /Sonos/Groups</c>.
/// </summary>
public sealed class GroupsResponse
{
    /// <summary>Gets current groups.</summary>
    public IReadOnlyList<GroupInfo> Groups { get; init; } = [];
}
