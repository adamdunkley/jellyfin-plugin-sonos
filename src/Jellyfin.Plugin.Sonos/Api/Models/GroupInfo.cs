using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// A Sonos group (coordinator plus members).
/// </summary>
public sealed class GroupInfo
{
    /// <summary>
    /// Gets the group id.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name, e.g. Room A + Room B.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the coordinator player id.
    /// </summary>
    public string CoordinatorId { get; init; } = string.Empty;

    /// <summary>
    /// Gets member player ids, including the coordinator.
    /// </summary>
    public IReadOnlyList<string> MemberIds { get; init; } = [];

    /// <summary>
    /// Gets the group playback state.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlaybackState PlaybackState { get; init; }
}
