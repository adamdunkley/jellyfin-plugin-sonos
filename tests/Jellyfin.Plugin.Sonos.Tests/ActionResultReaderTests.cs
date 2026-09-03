using Jellyfin.Plugin.Sonos.Api;
using Jellyfin.Plugin.Sonos.Session;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class ActionResultReaderTests
{
    [Fact]
    public void ErrorCode_ReadsProblemError()
    {
        var result = ProblemResults.Create(StatusCodes.Status400BadRequest, "NotAudio", "Item is not audio");

        Assert.Equal("NotAudio", ActionResultReader.ErrorCode(result));
        Assert.Equal("Item is not audio", ActionResultReader.Message(result));
        Assert.True(ActionResultReader.IsNotAudio(result));
        Assert.False(ActionResultReader.IsSuccess(result));
    }

    [Fact]
    public void SessionPlayPath_IgnoresNotAudioInsteadOfThrowing()
    {
        var result = ProblemResults.Create(StatusCodes.Status400BadRequest, "NotAudio", "Item is not audio");

        Assert.True(ActionResultReader.ShouldIgnorePlayFailure(result));
    }

    [Fact]
    public void SessionPlayPath_DoesNotIgnoreOtherFailures()
    {
        var result = ProblemResults.Create(StatusCodes.Status409Conflict, "PlayerUnavailable", "Speaker is offline");

        Assert.Equal("PlayerUnavailable", ActionResultReader.ErrorCode(result));
        Assert.False(ActionResultReader.IsNotAudio(result));
        Assert.False(ActionResultReader.ShouldIgnorePlayFailure(result));
        Assert.Equal("Speaker is offline", ActionResultReader.Message(result));
    }
}
