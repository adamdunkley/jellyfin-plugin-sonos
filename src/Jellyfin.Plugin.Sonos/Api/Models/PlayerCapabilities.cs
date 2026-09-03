using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// Hardware playback capabilities used by the transcode planner.
/// </summary>
public sealed class PlayerCapabilities
{
    /// <summary>
    /// Gets a value indicating whether gapless transitions are supported.
    /// </summary>
    public bool Gapless { get; init; }

    /// <summary>
    /// Gets a value indicating whether crossfade is supported.
    /// </summary>
    public bool Crossfade { get; init; }

    /// <summary>
    /// Gets the maximum sample rate in Hz.
    /// </summary>
    public int MaxSampleRate { get; init; } = 48000;

    /// <summary>
    /// Gets the maximum PCM bit depth.
    /// </summary>
    public int MaxBitDepth { get; init; } = 16;

    /// <summary>
    /// Gets codecs the player can decode without transcoding.
    /// </summary>
    public IReadOnlyList<string> NativeCodecs { get; init; } = ["flac", "mp3", "aac"];

    /// <summary>
    /// Gets the S2 capability profile. This is the only profile; transcode planning always uses it.
    /// </summary>
    public static PlayerCapabilities S2Default { get; } = new()
    {
        Gapless = true,
        Crossfade = true,
        MaxSampleRate = 48000,
        MaxBitDepth = 16,
        NativeCodecs = ["flac", "mp3", "aac"]
    };
}
