namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Why a track cannot be direct-played.
/// </summary>
public enum TranscodeReason
{
    /// <summary>Direct play.</summary>
    None,

    /// <summary>Container is not in the Sonos matrix.</summary>
    ContainerNotSupported,

    /// <summary>Codec is not native.</summary>
    CodecNotSupported,

    /// <summary>Sample rate above the player max.</summary>
    SampleRateTooHigh,

    /// <summary>Bit depth above the player max.</summary>
    BitDepthTooHigh,

    /// <summary>More than two channels.</summary>
    ChannelCount,

    /// <summary>Album mixed 44.1/48 and gapless/crossfade needs one rate.</summary>
    AlbumRateMatch
}
