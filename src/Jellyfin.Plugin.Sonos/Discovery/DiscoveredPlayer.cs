using System;
using Jellyfin.Plugin.Sonos.Api.Models;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// Internal discovered-player record, including fields not always exposed on the public API.
/// </summary>
public sealed class DiscoveredPlayer
{
    /// <summary>
    /// Gets or sets the RINCON UUID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the room name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hardware model code.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable model name.
    /// </summary>
    public string ModelDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the LAN IP.
    /// </summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current group id.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this player is the group coordinator.
    /// </summary>
    public bool IsCoordinator { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the player recently responded.
    /// </summary>
    public bool Available { get; set; } = true;

    /// <summary>
    /// Gets or sets household id from the LAN info API.
    /// </summary>
    public string? HouseholdId { get; set; }

    /// <summary>
    /// Gets or sets the LAN Control websocket URL.
    /// </summary>
    public string? WebsocketUrl { get; set; }

    /// <summary>
    /// Gets or sets last-seen UTC.
    /// </summary>
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets consecutive discovery misses.
    /// </summary>
    public int MissedCycles { get; set; }

    /// <summary>
    /// Gets or sets last known volume 0-100.
    /// </summary>
    public int? Volume { get; set; }

    /// <summary>
    /// Gets or sets last known mute.
    /// </summary>
    public bool? Muted { get; set; }

    /// <summary>
    /// Maps to the public API DTO.
    /// </summary>
    /// <returns>A <see cref="PlayerInfo"/>.</returns>
    public PlayerInfo ToInfo()
    {
        return new PlayerInfo
        {
            Id = Id,
            Name = Name,
            Model = Model,
            ModelDisplayName = ModelDisplayName,
            Ip = Ip,
            GroupId = GroupId,
            IsCoordinator = IsCoordinator,
            Available = Available,
            Volume = Volume,
            Muted = Muted,
            Capabilities = PlayerCapabilities.S2Default
        };
    }
}
