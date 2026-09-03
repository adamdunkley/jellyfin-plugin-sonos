using System;
using System.Collections.Generic;
using System.Net;

namespace Jellyfin.Plugin.Sonos.Util;

/// <summary>
/// Parses comma-separated IPv4/IPv6 addresses from plugin configuration.
/// </summary>
public static class IpListParser
{
    /// <summary>
    /// Splits and validates seed player IPs.
    /// </summary>
    /// <param name="value">Raw config string.</param>
    /// <returns>Distinct valid IP strings.</returns>
    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IPAddress.TryParse(part, out var address))
            {
                continue;
            }

            var formatted = address.ToString();
            if (seen.Add(formatted))
            {
                result.Add(formatted);
            }
        }

        return result;
    }
}
