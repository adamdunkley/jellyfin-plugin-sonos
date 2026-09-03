using System;
using Jellyfin.Plugin.Sonos.Streaming;
using Xunit;

namespace Jellyfin.Plugin.Sonos.Tests;

public sealed class StreamTokenTests
{
    [Fact]
    public void PackUnpack_RoundTrips()
    {
        var service = new StreamTokenService(new byte[32]);
        var payload = new StreamTokenPayload
        {
            ItemId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            UserId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Container = "flac",
            SampleRate = 44100,
            BitDepth = 16,
            DirectPlay = true,
            ExpiryUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            PlayerId = "RINCON_A"
        };

        var token = service.Mint(payload);
        Assert.True(service.TryUnpack(token, out var unpacked, out var expired));
        Assert.False(expired);
        Assert.Equal(payload.ItemId, unpacked.ItemId);
        Assert.Equal(payload.UserId, unpacked.UserId);
        Assert.Equal("flac", unpacked.Container);
        Assert.Equal(44100, unpacked.SampleRate);
        Assert.Equal("RINCON_A", unpacked.PlayerId);
        Assert.True(unpacked.DirectPlay);
    }

    [Fact]
    public void Unpack_ExpiredToken_SetsExpired()
    {
        var service = new StreamTokenService(new byte[32]);
        var token = service.Mint(new StreamTokenPayload
        {
            ItemId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Container = "mp3",
            SampleRate = 48000,
            BitDepth = 16,
            DirectPlay = true,
            ExpiryUnix = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds()
        });

        Assert.True(service.TryUnpack(token, out _, out var expired));
        Assert.True(expired);
    }

    [Fact]
    public void Unpack_TamperedToken_Fails()
    {
        var service = new StreamTokenService(new byte[32]);
        var token = service.Mint(new StreamTokenPayload
        {
            ItemId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ExpiryUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        Assert.False(service.TryUnpack(token + "x", out _, out _));
    }

    [Fact]
    public void DifferentCodecInToken_IsDistinct()
    {
        var service = new StreamTokenService(new byte[32]);
        var item = Guid.NewGuid();
        var user = Guid.NewGuid();
        var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var a = service.Mint(new StreamTokenPayload { ItemId = item, UserId = user, Container = "flac", SampleRate = 44100, ExpiryUnix = expiry, DirectPlay = true });
        var b = service.Mint(new StreamTokenPayload { ItemId = item, UserId = user, Container = "flac", SampleRate = 48000, ExpiryUnix = expiry, DirectPlay = false });
        Assert.NotEqual(a, b);
    }
}
