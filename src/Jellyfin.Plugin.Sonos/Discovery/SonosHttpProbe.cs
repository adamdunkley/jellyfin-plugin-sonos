using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// Unicast HTTP probes against a Sonos player (UPnP :1400 and LAN API :1443).
/// </summary>
public sealed class SonosHttpProbe : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _upnp;
    private readonly HttpClient _lan;
    private readonly ILogger<SonosHttpProbe> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosHttpProbe"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public SonosHttpProbe(ILogger<SonosHttpProbe> logger)
    {
        _logger = logger;
        _upnp = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var lanHandler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            SslOptions =
            {
                RemoteCertificateValidationCallback = static (_, _, _, _) => true
            }
        };
        _lan = new HttpClient(lanHandler) { Timeout = TimeSpan.FromSeconds(8) };
        _lan.DefaultRequestHeaders.TryAddWithoutValidation("X-Sonos-Api-Key", SonosConstants.LocalApiToken);
    }

    /// <summary>
    /// Fetches device_description.xml.
    /// </summary>
    /// <param name="ip">Player IP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed description or null.</returns>
    public async Task<DeviceDescription?> GetDeviceDescriptionAsync(string ip, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var xml = await _upnp.GetStringAsync(new Uri($"http://{ip}:1400/xml/device_description.xml"), cts.Token).ConfigureAwait(false);
            return DeviceDescriptionParser.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "device_description failed for {Ip}", ip);
            return null;
        }
    }

    /// <summary>
    /// Fetches LAN local player info (S2).
    /// </summary>
    /// <param name="ip">Player IP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Info JSON or null if not S2 / unreachable.</returns>
    public async Task<LanPlayerInfo?> GetLanInfoAsync(string ip, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            return await _lan.GetFromJsonAsync<LanPlayerInfo>(
                new Uri($"https://{ip}:1443/api/v1/players/local/info"),
                JsonOptions,
                cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LAN info failed for {Ip}", ip);
            return null;
        }
    }

    /// <summary>
    /// SOAP GetZoneGroupState.
    /// </summary>
    /// <param name="ip">Player IP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Inner XML or empty.</returns>
    public async Task<string> GetZoneGroupStateXmlAsync(string ip, CancellationToken cancellationToken)
    {
        const string body =
            """<u:GetZoneGroupState xmlns:u="urn:schemas-upnp-org:service:ZoneGroupTopology:1"></u:GetZoneGroupState>""";
        try
        {
            return await SoapPostAsync(
                ip,
                "/ZoneGroupTopology/Control",
                "urn:schemas-upnp-org:service:ZoneGroupTopology:1#GetZoneGroupState",
                body,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetZoneGroupState failed for {Ip}", ip);
            return string.Empty;
        }
    }

    /// <summary>
    /// Posts a SOAP action to the player.
    /// </summary>
    /// <param name="ip">Player IP.</param>
    /// <param name="path">Control URL path.</param>
    /// <param name="soapAction">SOAPACTION header value.</param>
    /// <param name="innerBody">Inner XML body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response XML.</returns>
    public async Task<string> SoapPostAsync(
        string ip,
        string path,
        string soapAction,
        string innerBody,
        CancellationToken cancellationToken)
    {
        var envelope =
            "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>"
            + innerBody
            + "</s:Body></s:Envelope>";

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"http://{ip}:1400{path}"))
        {
            Content = new StringContent(envelope, System.Text.Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{soapAction}\"");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await _upnp.SendAsync(request, cts.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"SOAP {soapAction} failed with {(int)response.StatusCode}", null, response.StatusCode);
        }

        return text;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _upnp.Dispose();
        _lan.Dispose();
    }
}
