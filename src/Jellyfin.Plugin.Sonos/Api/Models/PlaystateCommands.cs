namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// Known <c>POST /Sonos/Playstate</c> commands.
/// </summary>
public static class PlaystateCommands
{
    /// <summary>
    /// Returns true when <paramref name="command"/> is a supported playstate command.
    /// </summary>
    /// <param name="command">Command name.</param>
    /// <returns>True when known.</returns>
    public static bool IsKnown(string? command)
    {
        return command switch
        {
            "Play" or "Pause" or "Stop" or "Next" or "Previous" or "Seek"
                or "SetVolume" or "Mute" or "Unmute" or "SetRepeat" or "SetShuffle" or "SetCrossfade" => true,
            _ => false
        };
    }
}
