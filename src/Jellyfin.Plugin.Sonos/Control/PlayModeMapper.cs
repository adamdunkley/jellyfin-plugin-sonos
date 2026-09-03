using System;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Maps plugin repeat (None / All / One) onto LAN Control and SOAP play-mode values.
/// </summary>
public static class PlayModeMapper
{
    /// <summary>
    /// Splits a plugin repeat string into LAN <c>repeat</c> (all) and <c>repeatOne</c> flags.
    /// </summary>
    /// <param name="repeat">None, All, or One.</param>
    /// <param name="repeatAll">True when every queue item should loop.</param>
    /// <param name="repeatOne">True when the current track should loop.</param>
    public static void ToLanFlags(string? repeat, out bool repeatAll, out bool repeatOne)
    {
        if (string.Equals(repeat, "One", StringComparison.OrdinalIgnoreCase))
        {
            repeatAll = false;
            repeatOne = true;
            return;
        }

        if (string.Equals(repeat, "All", StringComparison.OrdinalIgnoreCase))
        {
            repeatAll = true;
            repeatOne = false;
            return;
        }

        repeatAll = false;
        repeatOne = false;
    }

    /// <summary>
    /// Maps plugin repeat and shuffle onto a SOAP <c>SetPlayMode</c> value.
    /// </summary>
    /// <param name="repeat">None, All, or One.</param>
    /// <param name="shuffle">Whether shuffle is on.</param>
    /// <returns>A UPnP play-mode token.</returns>
    public static string ToSoapPlayMode(string? repeat, bool shuffle)
    {
        ToLanFlags(repeat, out var repeatAll, out var repeatOne);
        if (shuffle)
        {
            if (repeatOne)
            {
                return "SHUFFLE_REPEAT_ONE";
            }

            return repeatAll ? "SHUFFLE" : "SHUFFLE_NOREPEAT";
        }

        if (repeatOne)
        {
            return "REPEAT_ONE";
        }

        return repeatAll ? "REPEAT_ALL" : "NORMAL";
    }
}
