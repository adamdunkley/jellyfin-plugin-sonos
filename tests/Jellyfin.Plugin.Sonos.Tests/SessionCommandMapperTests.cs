using System;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Session;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class SessionCommandMapperTests
{
    private static readonly Guid TrackA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TrackB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void MapPlay_PlayNow_UsesStartIndexAndTicks()
    {
        var mapped = SessionCommandMapper.MapPlay(
            new PlayRequest
            {
                ItemIds = [TrackA, TrackB],
                PlayCommand = PlayCommand.PlayNow,
                StartIndex = 1,
                StartPositionTicks = 123
            },
            "RINCON_A");

        Assert.NotNull(mapped.Play);
        Assert.Null(mapped.Add);
        Assert.Equal("RINCON_A", mapped.Play!.TargetId);
        Assert.Equal([TrackA, TrackB], mapped.Play.ItemIds);
        Assert.Equal(1, mapped.Play.StartIndex);
        Assert.Equal(123, mapped.Play.StartPositionTicks);
    }

    [Fact]
    public void MapPlay_PlayNext_AddsNext()
    {
        var mapped = SessionCommandMapper.MapPlay(
            new PlayRequest { ItemIds = [TrackA], PlayCommand = PlayCommand.PlayNext },
            "RINCON_A");

        Assert.Null(mapped.Play);
        Assert.NotNull(mapped.Add);
        Assert.Equal("Next", mapped.Add!.Mode);
        Assert.Equal("RINCON_A", mapped.Add.TargetId);
    }

    [Fact]
    public void MapPlay_PlayLast_AddsLast()
    {
        var mapped = SessionCommandMapper.MapPlay(
            new PlayRequest { ItemIds = [TrackA], PlayCommand = PlayCommand.PlayLast },
            "RINCON_A");

        Assert.Equal("Last", mapped.Add!.Mode);
    }

    [Theory]
    [InlineData(PlaystateCommand.Stop, "Stop")]
    [InlineData(PlaystateCommand.Pause, "Pause")]
    [InlineData(PlaystateCommand.Unpause, "Play")]
    [InlineData(PlaystateCommand.NextTrack, "Next")]
    [InlineData(PlaystateCommand.PreviousTrack, "Previous")]
    [InlineData(PlaystateCommand.Seek, "Seek")]
    public void MapPlaystate_KnownCommands(PlaystateCommand command, string expected)
    {
        var mapped = SessionCommandMapper.MapPlaystate(
            new MediaBrowser.Model.Session.PlaystateRequest { Command = command, SeekPositionTicks = 50 },
            "RINCON_A",
            PlaybackState.Playing);

        Assert.NotNull(mapped);
        Assert.Equal(expected, mapped!.Command);
        Assert.Equal("RINCON_A", mapped.TargetId);
        Assert.Equal(50, mapped.PositionTicks);
    }

    [Fact]
    public void MapPlaystate_PlayPause_TogglesFromPlaying()
    {
        var mapped = SessionCommandMapper.MapPlaystate(
            new MediaBrowser.Model.Session.PlaystateRequest { Command = PlaystateCommand.PlayPause },
            "RINCON_A",
            PlaybackState.Playing);

        Assert.Equal("Pause", mapped!.Command);
    }

    [Fact]
    public void MapPlaystate_PlayPause_TogglesFromPaused()
    {
        var mapped = SessionCommandMapper.MapPlaystate(
            new MediaBrowser.Model.Session.PlaystateRequest { Command = PlaystateCommand.PlayPause },
            "RINCON_A",
            PlaybackState.Paused);

        Assert.Equal("Play", mapped!.Command);
    }

    [Fact]
    public void MapGeneralCommand_SetVolume()
    {
        var command = new GeneralCommand { Name = GeneralCommandType.SetVolume };
        command.Arguments["Volume"] = "42";

        var mapped = SessionCommandMapper.MapGeneralCommand(command, "RINCON_A", null);

        Assert.Equal("SetVolume", mapped!.Command);
        Assert.Equal(42, mapped.Volume);
    }

    [Fact]
    public void MapGeneralCommand_SetVolume_AcceptsCamelCaseKey()
    {
        var command = new GeneralCommand { Name = GeneralCommandType.SetVolume };
        command.Arguments["volume"] = "7";

        var mapped = SessionCommandMapper.MapGeneralCommand(command, "RINCON_A", null);

        Assert.Equal(7, mapped!.Volume);
    }

    [Fact]
    public void MapGeneralCommand_ToggleMute_UsesQueue()
    {
        var command = new GeneralCommand { Name = GeneralCommandType.ToggleMute };
        var muted = SessionCommandMapper.MapGeneralCommand(command, "RINCON_A", new QueueResponse { Muted = true });
        var unmuted = SessionCommandMapper.MapGeneralCommand(command, "RINCON_A", new QueueResponse { Muted = false });

        Assert.Equal("Unmute", muted!.Command);
        Assert.Equal("Mute", unmuted!.Command);
    }

    [Fact]
    public void MapGeneralCommand_RepeatAndShuffle()
    {
        var repeat = new GeneralCommand { Name = GeneralCommandType.SetRepeatMode };
        repeat.Arguments["RepeatMode"] = "RepeatAll";
        var shuffle = new GeneralCommand { Name = GeneralCommandType.SetShuffleQueue };
        shuffle.Arguments["ShuffleMode"] = "Shuffle";

        var mappedRepeat = SessionCommandMapper.MapGeneralCommand(repeat, "RINCON_A", null);
        var mappedShuffle = SessionCommandMapper.MapGeneralCommand(shuffle, "RINCON_A", null);

        Assert.Equal("SetRepeat", mappedRepeat!.Command);
        Assert.Equal("All", mappedRepeat.Repeat);
        Assert.Equal("SetShuffle", mappedShuffle!.Command);
        Assert.True(mappedShuffle.Shuffle);
    }

    [Fact]
    public void ToRepeatMode_MapsPluginValues()
    {
        Assert.Equal(RepeatMode.RepeatNone, SessionCommandMapper.ToRepeatMode("None"));
        Assert.Equal(RepeatMode.RepeatAll, SessionCommandMapper.ToRepeatMode("All"));
        Assert.Equal(RepeatMode.RepeatOne, SessionCommandMapper.ToRepeatMode("One"));
    }
}
