namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Probe of a library audio stream (testable without Jellyfin entities).
/// </summary>
public sealed class AudioStreamInfo
{
    /// <summary>Gets the codec (flac, mp3, aac, dsd, alac, …).</summary>
    public string Codec { get; init; } = string.Empty;

    /// <summary>Gets the container (flac, mp3, m4a, mp4, dsf, …).</summary>
    public string Container { get; init; } = string.Empty;

    /// <summary>Gets sample rate in Hz.</summary>
    public int SampleRate { get; init; }

    /// <summary>Gets bit depth.</summary>
    public int BitDepth { get; init; }

    /// <summary>Gets channel count.</summary>
    public int Channels { get; init; }
}
