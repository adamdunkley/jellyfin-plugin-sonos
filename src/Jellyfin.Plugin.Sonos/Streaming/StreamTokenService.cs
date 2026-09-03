using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// HMAC stream tokens. Never log the token string.
/// </summary>
public sealed class StreamTokenService
{
    private readonly byte[] _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamTokenService"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin paths used to persist the HMAC key.</param>
    public StreamTokenService(IApplicationPaths applicationPaths)
        : this(LoadOrCreateKey(applicationPaths))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamTokenService"/> class.
    /// </summary>
    /// <param name="key">HMAC key.</param>
    internal StreamTokenService(byte[] key)
    {
        _key = key;
    }

    /// <summary>
    /// Mints a URL-safe token.
    /// </summary>
    /// <param name="payload">Token payload.</param>
    /// <returns>Token string.</returns>
    public string Mint(StreamTokenPayload payload)
    {
        var canonical = Canonical(payload);
        var signature = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(canonical));
        var packed = Encoding.UTF8.GetBytes(canonical + "\n" + Convert.ToBase64String(signature));
        return Base64UrlEncode(packed);
    }

    /// <summary>
    /// Unpacks and verifies a token.
    /// </summary>
    /// <param name="token">Token string.</param>
    /// <param name="payload">Payload when valid.</param>
    /// <param name="expired">True when signature is valid but expired.</param>
    /// <returns>True when the signature is valid.</returns>
    public bool TryUnpack(string token, out StreamTokenPayload payload, out bool expired)
    {
        payload = null!;
        expired = false;
        try
        {
            var packed = Base64UrlDecode(token);
            var text = Encoding.UTF8.GetString(packed);
            var split = text.LastIndexOf('\n');
            if (split <= 0)
            {
                return false;
            }

            var canonical = text[..split];
            var sigB64 = text[(split + 1)..];
            var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(canonical));
            var actual = Convert.FromBase64String(sigB64);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                return false;
            }

            payload = ParseCanonical(canonical);
            expired = payload.ExpiryUnix < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Canonical(StreamTokenPayload payload)
    {
        return string.Join(
            '|',
            payload.ItemId.ToString("N"),
            payload.UserId.ToString("N"),
            payload.Container,
            payload.SampleRate.ToString(CultureInfo.InvariantCulture),
            payload.BitDepth.ToString(CultureInfo.InvariantCulture),
            payload.DirectPlay ? "1" : "0",
            payload.ExpiryUnix.ToString(CultureInfo.InvariantCulture),
            payload.PlayerId ?? string.Empty);
    }

    private static StreamTokenPayload ParseCanonical(string canonical)
    {
        var parts = canonical.Split('|');
        if (parts.Length != 8)
        {
            throw new FormatException("token");
        }

        return new StreamTokenPayload
        {
            ItemId = Guid.Parse(parts[0]),
            UserId = Guid.Parse(parts[1]),
            Container = parts[2],
            SampleRate = int.Parse(parts[3], CultureInfo.InvariantCulture),
            BitDepth = int.Parse(parts[4], CultureInfo.InvariantCulture),
            DirectPlay = parts[5] == "1",
            ExpiryUnix = long.Parse(parts[6], CultureInfo.InvariantCulture),
            PlayerId = string.IsNullOrEmpty(parts[7]) ? null : parts[7]
        };
    }

    private static byte[] LoadOrCreateKey(IApplicationPaths applicationPaths)
    {
        var dir = Path.Combine(applicationPaths.PluginConfigurationsPath, "Sonos");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "stream-hmac.key");
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, key);
        return key;
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string token)
    {
        var padded = token.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}
