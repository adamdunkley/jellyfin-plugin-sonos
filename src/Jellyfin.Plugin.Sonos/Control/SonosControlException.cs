using System;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Error talking to a Sonos player.
/// </summary>
public sealed class SonosControlException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SonosControlException"/> class.
    /// </summary>
    /// <param name="errorCode">Stable error code.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="httpStatus">Optional HTTP status from the player.</param>
    public SonosControlException(string errorCode, string message, int? httpStatus = null)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
    }

    /// <summary>Gets the stable error code.</summary>
    public string ErrorCode { get; }

    /// <summary>Gets the HTTP status when known.</summary>
    public int? HttpStatus { get; }

    /// <summary>
    /// Returns true when the speaker reports that this group has no usable Cloud Queue session.
    /// </summary>
    /// <returns>True when Queue/Play should create a session and retry, or map to <c>PlayerUnavailable</c>.</returns>
    public bool IsMissingPlaybackSession()
    {
        if (string.Equals(ErrorCode, "ERROR_INVALID_OBJECT_ID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ErrorCode, "ERROR_SESSION_EVICTED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var message = Message ?? string.Empty;
        if (message.Contains("no session on this player", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (string.Equals(ErrorCode, "sessionError", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ErrorCode, "playbackError", StringComparison.OrdinalIgnoreCase))
               && message.Contains("no session", StringComparison.OrdinalIgnoreCase);
    }
}
