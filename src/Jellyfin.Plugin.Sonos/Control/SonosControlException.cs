using System;
using System.Collections.Generic;

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
    /// <param name="details">Optional safe details for problem JSON.</param>
    public SonosControlException(
        string errorCode,
        string message,
        int? httpStatus = null,
        IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
        Details = details;
    }

    /// <summary>Gets the stable error code.</summary>
    public string ErrorCode { get; }

    /// <summary>Gets the HTTP status when known.</summary>
    public int? HttpStatus { get; }

    /// <summary>Gets optional safe details for problem JSON.</summary>
    public IReadOnlyDictionary<string, object?>? Details { get; }

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

        // Firmware returns this when loadCloudQueue is called with an empty/stale sessionId.
        if (string.Equals(ErrorCode, "ERROR_MISSING_PARAMETERS", StringComparison.OrdinalIgnoreCase)
            && message.Contains("sessionId", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (string.Equals(ErrorCode, "sessionError", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ErrorCode, "playbackError", StringComparison.OrdinalIgnoreCase))
               && message.Contains("no session", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when Sonos rejected <c>createGroup</c> because <c>musicContextGroupId</c> could not be copied.
    /// Callers should retry without a music context and reload Cloud Queue afterwards.
    /// </summary>
    /// <returns>True when a createGroup-without-context retry is appropriate.</returns>
    public bool IsMusicContextCopyFailure()
    {
        var message = Message ?? string.Empty;
        if (!message.Contains("music context", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("musicContext", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return message.Contains("cannot be copied", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ErrorCode, "ERROR_PLAYBACK_FAILED", StringComparison.OrdinalIgnoreCase);
    }
}
