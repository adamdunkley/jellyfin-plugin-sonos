using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Groups</c> body. Listed players (and anyone already grouped with them) join one group.
/// The first id is the coordinator unless <see cref="CoordinatorId"/> is set.
/// </summary>
public sealed class CreateGroupRequest
{
    /// <summary>Gets player ids to include.</summary>
    public IReadOnlyList<string> PlayerIds { get; init; } = [];

    /// <summary>Gets the coordinator player id. Defaults to the first <see cref="PlayerIds"/> entry.</summary>
    public string? CoordinatorId { get; init; }
}
