using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Groups/{id}/Members</c> body.
/// </summary>
public sealed class ModifyGroupMembersRequest
{
    /// <summary>Gets player ids to add.</summary>
    public IReadOnlyList<string> PlayerIdsToAdd { get; init; } = [];

    /// <summary>Gets player ids to remove.</summary>
    public IReadOnlyList<string> PlayerIdsToRemove { get; init; } = [];
}
