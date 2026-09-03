using System;
using System.Diagnostics;
using System.IO;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Session;
using Jellyfin.Plugin.Sonos.Web;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

/// <summary>
/// Contract for handing playback between the jellyfin-web now-playing bar and a Sonos speaker.
/// Sonos transfers use POST /Sonos/Queue/Play (not sessionPlayer PlayNow). Local bind uses setDefaultPlayerActive.
/// </summary>
public sealed class PlayerHandoffTests
{
    private static readonly Guid TrackA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TrackB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void QueuePlay_CarriesStartIndexAndPositionTicks()
    {
        // sessionPlayer.play() builds sendPlayCommand(..., 'PlayNow') with ItemIds, StartIndex, StartPositionTicks.
        var mapped = SessionCommandMapper.MapPlay(
            new PlayRequest
            {
                ItemIds = [TrackA, TrackB],
                PlayCommand = PlayCommand.PlayNow,
                StartIndex = 1,
                StartPositionTicks = 9_000_000
            },
            "RINCON_TESTPLAYER1");

        Assert.NotNull(mapped.Play);
        Assert.Equal("RINCON_TESTPLAYER1", mapped.Play!.TargetId);
        Assert.Equal([TrackA, TrackB], mapped.Play.ItemIds);
        Assert.Equal(1, mapped.Play.StartIndex);
        Assert.Equal(9_000_000, mapped.Play.StartPositionTicks);
    }

    [Fact]
    public void PauseOnBoundSonosSession_DoesNotSwitchTheNowPlayingBar()
    {
        // Playstate Pause/Stop while Remote Control is still bound only affects the speaker.
        // The next Unpause from the now-playing bar stays on that same session.
        var pause = SessionCommandMapper.MapPlaystate(
            new MediaBrowser.Model.Session.PlaystateRequest { Command = PlaystateCommand.Pause },
            "RINCON_TESTPLAYER1",
            PlaybackState.Playing);
        var unpause = SessionCommandMapper.MapPlaystate(
            new MediaBrowser.Model.Session.PlaystateRequest { Command = PlaystateCommand.Unpause },
            "RINCON_TESTPLAYER1",
            PlaybackState.Paused);

        Assert.Equal("Pause", pause!.Command);
        Assert.Equal("Play", unpause!.Command);
        Assert.Equal("RINCON_TESTPLAYER1", unpause.TargetId);
    }

    [Fact]
    public void StopCommand_TargetsTheCoordinatorNotTheBrowser()
    {
        var mapped = SessionCommandMapper.MapPlaystate(
            new MediaBrowser.Model.Session.PlaystateRequest { Command = PlaystateCommand.Stop },
            "RINCON_TESTPLAYER1",
            PlaybackState.Playing);

        Assert.Equal("Stop", mapped!.Command);
        Assert.Equal("RINCON_TESTPLAYER1", mapped.TargetId);
    }

    [Fact]
    public void NodeHandoffSpecsPass()
    {
        var jsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "js"));
        var testFile = Path.Combine(jsDir, "player-handoff.test.js");

        Assert.True(File.Exists(testFile), "Missing " + testFile);

        var start = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = "--test player-handoff.test.js",
            WorkingDirectory = jsDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(start);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);

        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
    }
}
