using System;
using System.Net;

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
}
