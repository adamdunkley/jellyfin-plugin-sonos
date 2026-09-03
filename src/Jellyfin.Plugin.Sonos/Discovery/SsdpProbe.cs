using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// SSDP M-SEARCH from an ephemeral UDP port. Never binds 1900.
/// </summary>
public sealed class SsdpProbe
{
    private const string Search =
        "M-SEARCH * HTTP/1.1\r\n" +
        "HOST: 239.255.255.250:1900\r\n" +
        "MAN: \"ssdp:discover\"\r\n" +
        "MX: 2\r\n" +
        "ST: urn:schemas-upnp-org:device:ZonePlayer:1\r\n" +
        "\r\n";

    private readonly ILogger<SsdpProbe> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SsdpProbe"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public SsdpProbe(ILogger<SsdpProbe> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sends M-SEARCH and collects LOCATION host IPs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovered IPs.</returns>
    public async Task<IReadOnlyList<string>> SearchAsync(CancellationToken cancellationToken)
    {
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.ReceiveTimeout = 2500;
            var multicast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            var payload = Encoding.UTF8.GetBytes(Search);
            await udp.SendAsync(payload, payload.Length, multicast).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(result.Buffer);
                var ip = ParseLocationHost(text) ?? result.RemoteEndPoint.Address.ToString();
                if (!string.IsNullOrEmpty(ip))
                {
                    ips.Add(ip);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSDP M-SEARCH failed (multicast may be blocked on this Docker network)");
        }

        return ips.ToArray();
    }

    private static string? ParseLocationHost(string response)
    {
        foreach (var line in response.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = line["LOCATION:".Length..].Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }
        }

        return null;
    }
}
