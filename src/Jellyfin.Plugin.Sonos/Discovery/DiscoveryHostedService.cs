using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Util;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// Background discovery of Sonos S2 players.
/// </summary>
public sealed class DiscoveryHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly ILogger<DiscoveryHostedService> _logger;
    private readonly PlayerRegistry _registry;
    private readonly SonosHttpProbe _probe;
    private readonly SsdpProbe _ssdp;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveryHostedService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="registry">Player registry.</param>
    /// <param name="probe">HTTP probe.</param>
    /// <param name="ssdp">SSDP probe.</param>
    public DiscoveryHostedService(
        ILogger<DiscoveryHostedService> logger,
        PlayerRegistry registry,
        SonosHttpProbe probe,
        SsdpProbe ssdp)
    {
        _logger = logger;
        _registry = registry;
        _probe = probe;
        _ssdp = ssdp;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sonos discovery hosted service started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Plugin.Instance?.Configuration.Enabled != false)
                {
                    await DiscoverOnceAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sonos discovery pass failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Sonos discovery hosted service stopped");
    }

    private async Task DiscoverOnceAsync(CancellationToken cancellationToken)
    {
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ip in IpListParser.Parse(Plugin.Instance?.Configuration.SeedPlayerIps))
        {
            pending.Add(ip);
        }

        try
        {
            foreach (var ip in await _ssdp.SearchAsync(cancellationToken).ConfigureAwait(false))
            {
                pending.Add(ip);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SSDP discovery skipped");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(4, 4);
        var tasks = pending.Select(ip => ProbeIpAsync(ip, pending, seen, gate, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
        _registry.CompleteCycle(seen);
    }

    private async Task ProbeIpAsync(
        string ip,
        HashSet<string> pending,
        HashSet<string> seen,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var description = await _probe.GetDeviceDescriptionAsync(ip, cancellationToken).ConfigureAwait(false);
            var topologyXml = await _probe.GetZoneGroupStateXmlAsync(ip, cancellationToken).ConfigureAwait(false);
            var members = ZoneGroupStateParser.Parse(topologyXml);
            var lan = await _probe.GetLanInfoAsync(ip, cancellationToken).ConfigureAwait(false);

            if (members.Count == 0 && description is not null)
            {
                members =
                [
                    new ZoneGroupMember
                    {
                        Id = description.Id,
                        ZoneName = description.RoomName,
                        Location = $"http://{ip}:1400/xml/device_description.xml",
                        SoftwareGeneration = lan is null ? 0 : 2,
                        GroupId = lan?.GroupId ?? description.Id,
                        CoordinatorId = description.Id
                    }
                ];
            }

            foreach (var member in members)
            {
                if (member.Invisible)
                {
                    continue;
                }

                var memberIp = HostFromLocation(member.Location) ?? ip;
                if (!string.Equals(memberIp, ip, StringComparison.OrdinalIgnoreCase)
                    && IPAddress.TryParse(memberIp, out _))
                {
                    lock (pending)
                    {
                        if (pending.Add(memberIp))
                        {
                            // Enqueued for a later pass; do not recurse unbounded this cycle.
                        }
                    }
                }

                var isProbedPlayer = string.Equals(memberIp, ip, StringComparison.OrdinalIgnoreCase);
                if (!ZoneGroupStateParser.IsS2(member) && !(isProbedPlayer && lan is not null))
                {
                    _logger.LogInformation("ignored S1 {Name} {Id}", member.ZoneName, member.Id);
                    continue;
                }

                seen.Add(member.Id);
                _registry.Upsert(new DiscoveredPlayer
                {
                    Id = member.Id,
                    Name = string.IsNullOrEmpty(member.ZoneName) ? description?.RoomName ?? member.Id : member.ZoneName,
                    Model = isProbedPlayer ? description?.ModelNumber ?? string.Empty : string.Empty,
                    ModelDisplayName = isProbedPlayer
                        ? (string.IsNullOrEmpty(description?.DisplayName) ? description?.ModelName ?? string.Empty : description.DisplayName)
                        : string.Empty,
                    Ip = memberIp,
                    GroupId = member.GroupId,
                    IsCoordinator = string.Equals(member.Id, member.CoordinatorId, StringComparison.OrdinalIgnoreCase),
                    HouseholdId = isProbedPlayer && !string.IsNullOrEmpty(lan?.HouseholdId) ? lan.HouseholdId : null,
                    WebsocketUrl = isProbedPlayer && !string.IsNullOrEmpty(lan?.WebsocketUrl) ? lan.WebsocketUrl : null
                });
                _logger.LogDebug("Discovered Sonos player {Name} ({Id}) at {Ip}", member.ZoneName, member.Id, memberIp);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Probe failed for {Ip}", ip);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string? HostFromLocation(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return null;
    }
}
