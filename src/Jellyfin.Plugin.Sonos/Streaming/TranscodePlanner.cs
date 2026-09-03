using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Configuration;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Decides direct-play vs transcode vs album resample.
/// </summary>
public static class TranscodePlanner
{
    private static readonly HashSet<string> NativeContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "mp3", "m4a", "mp4", "aac", "ogg"
    };

    /// <summary>
    /// Plans a single item against a coordinator profile.
    /// </summary>
    /// <param name="stream">Probed audio stream.</param>
    /// <param name="capabilities">Coordinator capabilities.</param>
    /// <param name="preferred">Preferred transcode codec.</param>
    /// <param name="forcedSampleRate">Album-wide sample rate, or null.</param>
    /// <returns>The decision.</returns>
    public static TranscodeDecision Plan(
        AudioStreamInfo stream,
        PlayerCapabilities capabilities,
        TranscodeCodec preferred,
        int? forcedSampleRate = null)
    {
        var codec = (stream.Codec ?? string.Empty).ToLowerInvariant();
        var container = (stream.Container ?? string.Empty).ToLowerInvariant().TrimStart('.');
        var native = capabilities.NativeCodecs ?? PlayerCapabilities.S2Default.NativeCodecs;
        var maxRate = capabilities.MaxSampleRate > 0 ? capabilities.MaxSampleRate : 48000;
        var maxDepth = capabilities.MaxBitDepth > 0 ? capabilities.MaxBitDepth : 16;

        TranscodeReason reason = TranscodeReason.None;
        if (codec is "dsd" or "dsf" or "dff" or "dst")
        {
            reason = TranscodeReason.CodecNotSupported;
        }
        else if (stream.Channels > 2)
        {
            reason = TranscodeReason.ChannelCount;
        }
        else if (!native.Contains(codec, StringComparer.OrdinalIgnoreCase) && codec is not "mpeg" and not "mp3")
        {
            if (codec is "aac" && native.Contains("aac", StringComparer.OrdinalIgnoreCase))
            {
                // ok
            }
            else
            {
                reason = TranscodeReason.CodecNotSupported;
            }
        }
        else if (!NativeContainers.Contains(container))
        {
            reason = TranscodeReason.ContainerNotSupported;
        }
        else if (stream.SampleRate > maxRate)
        {
            reason = TranscodeReason.SampleRateTooHigh;
        }
        else if (stream.BitDepth > maxDepth && codec == "flac")
        {
            reason = TranscodeReason.BitDepthTooHigh;
        }

        if (forcedSampleRate is int albumRate && stream.SampleRate != albumRate && reason == TranscodeReason.None)
        {
            reason = TranscodeReason.AlbumRateMatch;
        }

        if (reason == TranscodeReason.None)
        {
            return new TranscodeDecision
            {
                DirectPlay = true,
                Reason = TranscodeReason.None,
                Container = container is "m4a" or "mp4" ? "aac" : container,
                SampleRate = stream.SampleRate,
                BitDepth = stream.BitDepth > 0 ? stream.BitDepth : 16
            };
        }

        var targetRate = forcedSampleRate
            ?? (stream.SampleRate > 0 && stream.SampleRate % 44100 == 0 ? Math.Min(44100, maxRate) : Math.Min(48000, maxRate));
        if (stream.SampleRate is 44100 or 88200 or 176400)
        {
            targetRate = forcedSampleRate ?? 44100;
        }
        else if (stream.SampleRate >= 48000)
        {
            targetRate = forcedSampleRate ?? 48000;
        }

        var outContainer = preferred == TranscodeCodec.Aac ? "aac" : "flac";
        return new TranscodeDecision
        {
            DirectPlay = false,
            Reason = reason,
            Container = outContainer,
            SampleRate = targetRate,
            BitDepth = 16
        };
    }

    /// <summary>
    /// Picks a single sample rate for an album when 44.1 and 48 are mixed.
    /// </summary>
    /// <param name="streams">Queue item streams.</param>
    /// <returns>Forced rate or null.</returns>
    public static int? AlbumForcedSampleRate(IReadOnlyList<AudioStreamInfo> streams)
    {
        var rates = streams
            .Select(NormalizeFamily)
            .Where(r => r > 0)
            .Distinct()
            .ToArray();
        if (rates.Length <= 1)
        {
            return null;
        }

        var count441 = streams.Count(s => NormalizeFamily(s) == 44100);
        var count48 = streams.Count(s => NormalizeFamily(s) == 48000);
        if (count441 >= count48)
        {
            return 44100;
        }

        return 48000;
    }

    private static int NormalizeFamily(AudioStreamInfo stream)
    {
        if (stream.SampleRate <= 0)
        {
            return 0;
        }

        if (stream.SampleRate % 44100 == 0)
        {
            return 44100;
        }

        if (stream.SampleRate % 48000 == 0)
        {
            return 48000;
        }

        return stream.SampleRate;
    }
}
