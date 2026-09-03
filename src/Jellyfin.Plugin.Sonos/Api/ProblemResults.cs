using System.Collections.Generic;
using System.Net;
using Jellyfin.Plugin.Sonos.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Sonos.Api;

/// <summary>
/// Builds problem-style JSON results for the client API.
/// </summary>
public static class ProblemResults
{
    /// <summary>
    /// Creates an <see cref="ObjectResult"/> with a <see cref="ProblemError"/> body.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="error">Stable error code.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="details">Optional extra fields. Must not contain secrets or stack traces.</param>
    /// <returns>The object result.</returns>
    public static ObjectResult Create(
        HttpStatusCode statusCode,
        string error,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        return Create((int)statusCode, error, message, details);
    }

    /// <summary>
    /// Creates an <see cref="ObjectResult"/> with a <see cref="ProblemError"/> body.
    /// </summary>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="error">Stable error code.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="details">Optional extra fields. Must not contain secrets or stack traces.</param>
    /// <returns>The object result.</returns>
    public static ObjectResult Create(
        int statusCode,
        string error,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        return new ObjectResult(new ProblemError
        {
            Error = error,
            Message = message,
            Details = details
        })
        {
            StatusCode = statusCode
        };
    }
}
