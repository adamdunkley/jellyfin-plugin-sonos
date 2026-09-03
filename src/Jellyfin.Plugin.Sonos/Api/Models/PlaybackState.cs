namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// Playback state reported for a player or group.
/// </summary>
public enum PlaybackState
{
    /// <summary>Idle / no transport.</summary>
    Stopped,

    /// <summary>Actively playing.</summary>
    Playing,

    /// <summary>Paused with a loaded queue.</summary>
    Paused,

    /// <summary>Changing tracks or grouping.</summary>
    Transitioning
}
