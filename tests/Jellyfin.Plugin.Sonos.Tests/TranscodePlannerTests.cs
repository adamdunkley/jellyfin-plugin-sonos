using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Configuration;
using Jellyfin.Plugin.Sonos.Streaming;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class TranscodePlannerTests
{
    [Fact]
    public void Flac16_44_Copies()
    {
        var decision = TranscodePlanner.Plan(
            new AudioStreamInfo { Codec = "flac", Container = "flac", SampleRate = 44100, BitDepth = 16, Channels = 2 },
            PlayerCapabilities.S2Default,
            TranscodeCodec.Flac);
        Assert.True(decision.DirectPlay);
        Assert.Equal(TranscodeReason.None, decision.Reason);
    }

    [Fact]
    public void Flac96k_TranscodesTo48k()
    {
        var decision = TranscodePlanner.Plan(
            new AudioStreamInfo { Codec = "flac", Container = "flac", SampleRate = 96000, BitDepth = 24, Channels = 2 },
            PlayerCapabilities.S2Default,
            TranscodeCodec.Flac);
        Assert.False(decision.DirectPlay);
        Assert.Equal(TranscodeReason.SampleRateTooHigh, decision.Reason);
        Assert.Equal(48000, decision.SampleRate);
        Assert.Equal("flac", decision.Container);
    }

    [Fact]
    public void Dsd_Transcodes()
    {
        var decision = TranscodePlanner.Plan(
            new AudioStreamInfo { Codec = "dsd", Container = "dsf", SampleRate = 2822400, BitDepth = 1, Channels = 2 },
            PlayerCapabilities.S2Default,
            TranscodeCodec.Flac);
        Assert.False(decision.DirectPlay);
        Assert.Equal(TranscodeReason.CodecNotSupported, decision.Reason);
    }

    [Fact]
    public void MixedRateAlbum_Forces44100WhenMajority()
    {
        var streams = new[]
        {
            new AudioStreamInfo { SampleRate = 44100 },
            new AudioStreamInfo { SampleRate = 44100 },
            new AudioStreamInfo { SampleRate = 48000 }
        };
        Assert.Equal(44100, TranscodePlanner.AlbumForcedSampleRate(streams));
        var decision = TranscodePlanner.Plan(
            new AudioStreamInfo { Codec = "flac", Container = "flac", SampleRate = 48000, BitDepth = 16, Channels = 2 },
            PlayerCapabilities.S2Default,
            TranscodeCodec.Flac,
            44100);
        Assert.False(decision.DirectPlay);
        Assert.Equal(TranscodeReason.AlbumRateMatch, decision.Reason);
        Assert.Equal(44100, decision.SampleRate);
    }

    [Fact]
    public void Mp3_Copies()
    {
        var decision = TranscodePlanner.Plan(
            new AudioStreamInfo { Codec = "mp3", Container = "mp3", SampleRate = 44100, Channels = 2 },
            PlayerCapabilities.S2Default,
            TranscodeCodec.Flac);
        Assert.True(decision.DirectPlay);
    }
}
