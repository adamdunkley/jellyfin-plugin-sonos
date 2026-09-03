using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// A discovered Sonos player.
/// </summary>
public sealed class PlayerInfo
{
    /// <summary>
    /// Gets the RINCON UUID.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the room name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the hardware model code, e.g. S6.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the human-readable model name, e.g. Play:5.
    /// </summary>
    public string ModelDisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the player LAN IP.
    /// </summary>
    public string Ip { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current group id, if any.
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Gets a value indicating whether this player is the group coordinator.
    /// </summary>
    public bool IsCoordinator { get; init; }

    /// <summary>
    /// Gets a value indicating whether the player recently responded.
    /// </summary>
    public bool Available { get; init; }

    /// <summary>
    /// Gets live volume 0-100 when known.
    /// </summary>
    public int? Volume { get; init; }

    /// <summary>
    /// Gets live mute when known.
    /// </summary>
    public bool? Muted { get; init; }

    /// <summary>
    /// Gets playback capabilities for the coordinator profile.
    /// </summary>
    public PlayerCapabilities Capabilities { get; init; } = new();
}
