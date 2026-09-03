using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// One member of a Sonos zone group.
/// </summary>
public sealed class ZoneGroupMember
{
    /// <summary>Gets the RINCON UUID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the room name.</summary>
    public string ZoneName { get; init; } = string.Empty;

    /// <summary>Gets the device description URL.</summary>
    public string Location { get; init; } = string.Empty;

    /// <summary>Gets software generation (1 = S1, 2 = S2).</summary>
    public int SoftwareGeneration { get; init; }

    /// <summary>Gets software version string.</summary>
    public string SoftwareVersion { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the member is a hidden satellite.</summary>
    public bool Invisible { get; init; }

    /// <summary>Gets the group id this member belongs to.</summary>
    public string GroupId { get; init; } = string.Empty;

    /// <summary>Gets the coordinator RINCON.</summary>
    public string CoordinatorId { get; init; } = string.Empty;
}
