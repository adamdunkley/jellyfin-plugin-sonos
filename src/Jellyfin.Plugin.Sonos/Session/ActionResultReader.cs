using System;
using Jellyfin.Plugin.Sonos.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Sonos.Session;

/// <summary>
/// Reads <see cref="ActionResult"/> values returned by <see cref="SonosPlaybackService"/>.
/// </summary>
internal static class ActionResultReader
{
    public static bool IsSuccess(ActionResult result)
    {
        return result switch
        {
            OkObjectResult => true,
            OkResult => true,
            ObjectResult obj when obj.StatusCode is >= 200 and < 300 => true,
            StatusCodeResult status when status.StatusCode is >= 200 and < 300 => true,
            _ => false
        };
    }

    public static T? Value<T>(ActionResult result)
        where T : class
    {
        return result switch
        {
            OkObjectResult ok => ok.Value as T,
            ObjectResult obj => obj.Value as T,
            _ => null
        };
    }

    public static string Message(ActionResult result)
    {
        var problem = Value<ProblemError>(result);
        return problem?.Message ?? "Sonos command failed";
    }

    /// <summary>
    /// Gets the stable <see cref="ProblemError.Error"/> code, or an empty string.
    /// </summary>
    /// <param name="result">Action result.</param>
    /// <returns>The error code.</returns>
    public static string ErrorCode(ActionResult result)
    {
        var problem = Value<ProblemError>(result);
        return problem?.Error ?? string.Empty;
    }

    /// <summary>
    /// Returns true when playback rejected a non-audio library item.
    /// </summary>
    /// <param name="result">Action result.</param>
    /// <returns>Whether the result is <c>NotAudio</c>.</returns>
    public static bool IsNotAudio(ActionResult result)
    {
        return string.Equals(ErrorCode(result), "NotAudio", StringComparison.Ordinal);
    }

    /// <summary>
    /// Session Play To should swallow <c>NotAudio</c> instead of throwing HTTP 500.
    /// </summary>
    /// <param name="result">Action result.</param>
    /// <returns>Whether the play command should be ignored.</returns>
    public static bool ShouldIgnorePlayFailure(ActionResult result)
    {
        return !IsSuccess(result) && IsNotAudio(result);
    }
}
