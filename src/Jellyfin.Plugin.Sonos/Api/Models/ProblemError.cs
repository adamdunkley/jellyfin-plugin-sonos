using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// Problem-style error body for <c>/Sonos</c> APIs. Does not include stack traces.
/// </summary>
public sealed class ProblemError
{
    /// <summary>
    /// Gets a stable error code, e.g. <c>PlayerUnavailable</c>.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// Gets a human-readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets optional structured details (HTTP status from the speaker, etc.).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? Details { get; init; }
}
