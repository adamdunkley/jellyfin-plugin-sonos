using System;
using System.Net;
using System.Net.Sockets;

namespace Jellyfin.Plugin.Sonos.Util;

/// <summary>
/// Validates the Published base URL speakers will use to fetch audio and Cloud Queue.
/// </summary>
public static class PublishedUrlGuard
{
    /// <summary>
    /// Attempts to parse and validate a speaker-reachable base URL.
    /// </summary>
    /// <param name="value">Configured published base URL.</param>
    /// <param name="normalized">Trimmed URL without a trailing slash (except path root).</param>
    /// <param name="error">Stable error detail when invalid.</param>
    /// <returns>True when speakers can use this origin.</returns>
    public static bool TryValidate(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Published base URL is not set. Speakers cannot reach localhost or Docker bridge IPs.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Published base URL must be an absolute http(s) URI.";
            return false;
        }

        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            error = "Published base URL must not use loopback; speakers cannot fetch from localhost.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip) && IsUnusableForSpeakers(ip))
        {
            error = "Published base URL host is a loopback, link-local, or Docker-bridge address.";
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (string.IsNullOrEmpty(uri.PathAndQuery) || uri.AbsolutePath == "/")
        {
            normalized = uri.GetLeftPart(UriPartial.Authority);
        }

        return true;
    }

    private static bool IsUnusableForSpeakers(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal)
        {
            return true;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        // Docker / typical bridge: 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        return false;
    }
}
