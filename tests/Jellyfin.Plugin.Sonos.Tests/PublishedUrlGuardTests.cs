using Jellyfin.Plugin.Sonos.Util;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class PublishedUrlGuardTests
{
    [Theory]
    [InlineData("http://192.0.2.10:8096/media", "http://192.0.2.10:8096/media")]
    [InlineData("http://192.0.2.10:8096/media/", "http://192.0.2.10:8096/media")]
    public void AcceptsLanHttp(string input, string expected)
    {
        Assert.True(PublishedUrlGuard.TryValidate(input, out var normalized, out _));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://127.0.0.1:8096/media")]
    [InlineData("http://localhost:8096/media")]
    [InlineData("http://172.16.0.2:8096/media")]
    [InlineData("http://169.254.1.1:8096/media")]
    public void RejectsUnusableHosts(string? input)
    {
        Assert.False(PublishedUrlGuard.TryValidate(input, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
