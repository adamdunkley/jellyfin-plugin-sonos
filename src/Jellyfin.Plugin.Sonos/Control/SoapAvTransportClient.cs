using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Discovery;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// SOAP AVTransport + RenderingControl fallback used when LAN Control is unavailable.
/// </summary>
public sealed class SoapAvTransportClient : ISonosControlClient
{
    private readonly SonosHttpProbe _probe;
    private readonly CoordinatorGate _gate;
    private readonly ILogger<SoapAvTransportClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoapAvTransportClient"/> class.
    /// </summary>
    /// <param name="probe">HTTP probe used for SOAP posts.</param>
    /// <param name="gate">Per-coordinator lock.</param>
    /// <param name="logger">Logger.</param>
    public SoapAvTransportClient(SonosHttpProbe probe, CoordinatorGate gate, ILogger<SoapAvTransportClient> logger)
    {
        _probe = probe;
        _gate = gate;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SetAvTransportUriAsync(DiscoveredPlayer player, string uri, string metadataXml, CancellationToken cancellationToken)
    {
        var body =
            "<u:SetAVTransportURI xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">" +
            "<InstanceID>0</InstanceID>" +
            "<CurrentURI>" + XmlEscape(uri) + "</CurrentURI>" +
            "<CurrentURIMetaData>" + XmlEscape(metadataXml) + "</CurrentURIMetaData>" +
            "</u:SetAVTransportURI>";
        return InvokeAsync(player, "/MediaRenderer/AVTransport/Control", "urn:schemas-upnp-org:service:AVTransport:1#SetAVTransportURI", body, cancellationToken);
    }

    /// <inheritdoc />
    public Task PlayAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => AvAsync(player, "Play", "<Speed>1</Speed>", cancellationToken);

    /// <inheritdoc />
    public Task PauseAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => AvAsync(player, "Pause", string.Empty, cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => AvAsync(player, "Stop", string.Empty, cancellationToken);

    /// <inheritdoc />
    public Task NextAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => AvAsync(player, "Next", string.Empty, cancellationToken);

    /// <inheritdoc />
    public Task PreviousAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => AvAsync(player, "Previous", string.Empty, cancellationToken);

    /// <inheritdoc />
    public Task SeekAsync(DiscoveredPlayer player, TimeSpan position, CancellationToken cancellationToken)
    {
        var rel = string.Create(CultureInfo.InvariantCulture, $"{(int)position.TotalHours:D2}:{position.Minutes:D2}:{position.Seconds:D2}");
        return AvAsync(player, "Seek", "<Unit>REL_TIME</Unit><Target>" + rel + "</Target>", cancellationToken);
    }

    /// <inheritdoc />
    public Task SetVolumeAsync(DiscoveredPlayer player, int volume, CancellationToken cancellationToken)
    {
        volume = Math.Clamp(volume, 0, 100);
        var body =
            "<u:SetVolume xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\">" +
            "<InstanceID>0</InstanceID><Channel>Master</Channel><DesiredVolume>" +
            volume.ToString(CultureInfo.InvariantCulture) +
            "</DesiredVolume></u:SetVolume>";
        return InvokeAsync(player, "/MediaRenderer/RenderingControl/Control", "urn:schemas-upnp-org:service:RenderingControl:1#SetVolume", body, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetMuteAsync(DiscoveredPlayer player, bool muted, CancellationToken cancellationToken)
    {
        var body =
            "<u:SetMute xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\">" +
            "<InstanceID>0</InstanceID><Channel>Master</Channel><DesiredMute>" +
            (muted ? "1" : "0") +
            "</DesiredMute></u:SetMute>";
        return InvokeAsync(player, "/MediaRenderer/RenderingControl/Control", "urn:schemas-upnp-org:service:RenderingControl:1#SetMute", body, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(int Volume, bool Muted)> GetVolumeAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
    {
        var volXml = await InvokeAsync(
            player,
            "/MediaRenderer/RenderingControl/Control",
            "urn:schemas-upnp-org:service:RenderingControl:1#GetVolume",
            "<u:GetVolume xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\"><InstanceID>0</InstanceID><Channel>Master</Channel></u:GetVolume>",
            cancellationToken).ConfigureAwait(false);
        var muteXml = await InvokeAsync(
            player,
            "/MediaRenderer/RenderingControl/Control",
            "urn:schemas-upnp-org:service:RenderingControl:1#GetMute",
            "<u:GetMute xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\"><InstanceID>0</InstanceID><Channel>Master</Channel></u:GetMute>",
            cancellationToken).ConfigureAwait(false);
        _ = int.TryParse(ExtractTag(volXml, "CurrentVolume"), out var volume);
        var muted = ExtractTag(muteXml, "CurrentMute") is "1" or "true";
        return (volume, muted);
    }

    /// <inheritdoc />
    public async Task<TransportSnapshot> GetTransportAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
    {
        var stateXml = await AvAsync(player, "GetTransportInfo", string.Empty, cancellationToken).ConfigureAwait(false);
        var posXml = await AvAsync(player, "GetPositionInfo", string.Empty, cancellationToken).ConfigureAwait(false);
        var (volume, muted) = await GetVolumeAsync(player, cancellationToken).ConfigureAwait(false);
        var stateRaw = ExtractTag(stateXml, "CurrentTransportState");
        var rel = ExtractTag(posXml, "RelTime");
        return new TransportSnapshot
        {
            State = MapState(stateRaw),
            PositionTicks = ParseRelTime(rel).Ticks,
            Volume = volume,
            Muted = muted,
            CurrentUri = ExtractTag(posXml, "TrackURI")
        };
    }

    /// <inheritdoc />
    public Task LoadCloudQueueAsync(DiscoveredPlayer player, LoadCloudQueueRequest request, CancellationToken cancellationToken)
        => throw new SonosControlException("NotSupported", "SOAP client cannot loadCloudQueue");

    /// <inheritdoc />
    public Task RefreshCloudQueueAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetPlayModesAsync(DiscoveredPlayer player, string repeat, bool shuffle, bool crossfade, CancellationToken cancellationToken)
    {
        var playMode = PlayModeMapper.ToSoapPlayMode(repeat, shuffle);
        return AvAsync(player, "SetPlayMode", "<NewPlayMode>" + playMode + "</NewPlayMode>", cancellationToken);
    }

    /// <inheritdoc />
    public Task SkipToItemAsync(DiscoveredPlayer player, string itemId, int positionMillis, CancellationToken cancellationToken)
        => throw new SonosControlException("NotSupported", "SOAP client cannot skipToItem");

    /// <inheritdoc />
    public Task<GroupCommandResult> CreateGroupAsync(
        DiscoveredPlayer player,
        IReadOnlyList<string> playerIds,
        string? musicContextGroupId,
        CancellationToken cancellationToken)
        => throw new SonosControlException("NotSupported", "SOAP client cannot createGroup");

    /// <inheritdoc />
    public Task<GroupCommandResult> ModifyGroupMembersAsync(
        DiscoveredPlayer player,
        string groupId,
        IReadOnlyList<string> playerIdsToAdd,
        IReadOnlyList<string> playerIdsToRemove,
        CancellationToken cancellationToken)
        => throw new SonosControlException("NotSupported", "SOAP client cannot modifyGroupMembers");

    private Task<string> AvAsync(DiscoveredPlayer player, string action, string extra, CancellationToken cancellationToken)
    {
        var body = "<u:" + action + " xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\"><InstanceID>0</InstanceID>" + extra + "</u:" + action + ">";
        return InvokeAsync(player, "/MediaRenderer/AVTransport/Control", "urn:schemas-upnp-org:service:AVTransport:1#" + action, body, cancellationToken);
    }

    private Task<string> InvokeAsync(DiscoveredPlayer player, string path, string soapAction, string body, CancellationToken cancellationToken)
    {
        return _gate.RunAsync(
            player.Id,
            async () =>
            {
                try
                {
                    _logger.LogDebug("SOAP {Action} -> {Ip}", soapAction, player.Ip);
                    return await _probe.SoapPostAsync(player.Ip, path, soapAction, body, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new SonosControlException("LanAuthRequired", "Speaker returned 403", 403);
                }
                catch (Exception ex) when (ex is not SonosControlException)
                {
                    throw new SonosControlException("PlayerUnavailable", player.Name + " did not respond to " + soapAction, (int?)(ex as HttpRequestException)?.StatusCode);
                }
            },
            cancellationToken);
    }

    private static PlaybackState MapState(string raw) => raw switch
    {
        "PLAYING" => PlaybackState.Playing,
        "PAUSED_PLAYBACK" => PlaybackState.Paused,
        "TRANSITIONING" => PlaybackState.Transitioning,
        _ => PlaybackState.Stopped
    };

    private static TimeSpan ParseRelTime(string rel)
    {
        if (TimeSpan.TryParse(rel, CultureInfo.InvariantCulture, out var ts))
        {
            return ts;
        }

        return TimeSpan.Zero;
    }

    private static string ExtractTag(string xml, string localName)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var el in doc.Descendants())
            {
                if (string.Equals(el.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                {
                    return el.Value;
                }
            }
        }
        catch (Exception)
        {
            var match = Regex.Match(xml, "<" + localName + "[^>]*>([^<]*)</" + localName + ">", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return string.Empty;
    }

    private static string XmlEscape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
