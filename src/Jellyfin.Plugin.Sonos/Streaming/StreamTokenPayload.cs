using System;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Payload stored in a stream token.
/// </summary>
public sealed class StreamTokenPayload
{
    /// <summary>Gets the Jellyfin item id.</summary>
    public Guid ItemId { get; init; }

    /// <summary>Gets the user id.</summary>
    public Guid UserId { get; init; }

    /// <summary>Gets the container/codec (flac, mp3, aac).</summary>
    public string Container { get; init; } = "flac";

    /// <summary>Gets the sample rate.</summary>
    public int SampleRate { get; init; } = 44100;

    /// <summary>Gets the bit depth.</summary>
    public int BitDepth { get; init; } = 16;

    /// <summary>Gets a value indicating whether this is a direct-play file.</summary>
    public bool DirectPlay { get; init; }

    /// <summary>Gets expiry unix seconds.</summary>
    public long ExpiryUnix { get; init; }

    /// <summary>Gets optional player binding.</summary>
    public string? PlayerId { get; init; }
}
