namespace Jellyfin.Plugin.Sonos.Configuration;

/// <summary>
/// Preferred codec when a track cannot be direct-played.
/// </summary>
public enum TranscodeCodec
{
    /// <summary>
    /// FLAC 16-bit at 44.1 or 48 kHz.
    /// </summary>
    Flac,

    /// <summary>
    /// AAC-LC.
    /// </summary>
    Aac
}
