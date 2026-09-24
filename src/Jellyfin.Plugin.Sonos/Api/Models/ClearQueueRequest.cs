namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Queue/Clear</c> body.
/// </summary>
public sealed class ClearQueueRequest
{
    /// <summary>Gets the player or group id.</summary>
    public string TargetId { get; init; } = string.Empty;
}
