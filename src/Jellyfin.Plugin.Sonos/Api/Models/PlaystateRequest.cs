namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// <c>POST /Sonos/Playstate</c> body.
/// </summary>
public sealed class PlaystateRequest
{
    /// <summary>Gets the player or group id.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Gets the command name.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>Gets seek position in ticks (Seek).</summary>
    public long? PositionTicks { get; init; }

    /// <summary>Gets volume 0-100 (SetVolume).</summary>
    public int? Volume { get; init; }

    /// <summary>Gets repeat mode None|All|One (SetRepeat).</summary>
    public string? Repeat { get; init; }

    /// <summary>Gets shuffle (SetShuffle).</summary>
    public bool? Shuffle { get; init; }

    /// <summary>Gets crossfade (SetCrossfade).</summary>
    public bool? Crossfade { get; init; }
}
