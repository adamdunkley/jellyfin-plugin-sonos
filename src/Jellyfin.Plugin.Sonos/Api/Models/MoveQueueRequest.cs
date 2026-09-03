namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Queue/Move</c> body.
/// </summary>
public sealed class MoveQueueRequest
{
    /// <summary>Gets the player or group id.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Gets the source index.</summary>
    public int FromIndex { get; init; }

    /// <summary>Gets the destination index.</summary>
    public int ToIndex { get; init; }
}
