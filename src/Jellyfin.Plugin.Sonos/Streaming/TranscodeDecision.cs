using Jellyfin.Plugin.Sonos.Configuration;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Copy vs encode decision for one queue item.
/// </summary>
public sealed class TranscodeDecision
{
    /// <summary>Gets a value indicating whether the original file is sent.</summary>
    public bool DirectPlay { get; init; }

    /// <summary>Gets why transcode is required.</summary>
    public TranscodeReason Reason { get; init; }

    /// <summary>Gets the output container/codec.</summary>
    public string Container { get; init; } = "flac";

    /// <summary>Gets the output sample rate.</summary>
    public int SampleRate { get; init; } = 44100;

    /// <summary>Gets the output bit depth.</summary>
    public int BitDepth { get; init; } = 16;

    /// <summary>Gets the MIME type for HTTP.</summary>
    public string ContentType => Container switch
    {
        "mp3" => "audio/mpeg",
        "aac" or "m4a" or "mp4" => "audio/mp4",
        "ogg" => "audio/ogg",
        _ => "audio/flac"
    };
}
