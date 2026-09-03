using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Discovery;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// LAN Control first, SOAP AVTransport as last-resort fallback.
/// </summary>
public sealed class CompositeSonosControlClient : ISonosControlClient
{
    private readonly LanControlClient _lan;
    private readonly SoapAvTransportClient _soap;
    private readonly ILogger<CompositeSonosControlClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeSonosControlClient"/> class.
    /// </summary>
    /// <param name="lan">LAN Control client.</param>
    /// <param name="soap">SOAP fallback.</param>
    /// <param name="logger">Logger.</param>
    public CompositeSonosControlClient(
        LanControlClient lan,
        SoapAvTransportClient soap,
        ILogger<CompositeSonosControlClient> logger)
    {
        _lan = lan;
        _soap = soap;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SetAvTransportUriAsync(DiscoveredPlayer player, string uri, string metadataXml, CancellationToken cancellationToken)
        => _soap.SetAvTransportUriAsync(player, uri, metadataXml, cancellationToken);

    /// <inheritdoc />
    public Task PlayAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.PlayAsync(player, cancellationToken), () => _soap.PlayAsync(player, cancellationToken));

    /// <inheritdoc />
    public Task PauseAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.PauseAsync(player, cancellationToken), () => _soap.PauseAsync(player, cancellationToken));

    /// <inheritdoc />
    public Task StopAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.PauseAsync(player, cancellationToken), () => _soap.StopAsync(player, cancellationToken));

    /// <inheritdoc />
    public Task NextAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.NextAsync(player, cancellationToken), () => _soap.NextAsync(player, cancellationToken));

    /// <inheritdoc />
    public Task PreviousAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.PreviousAsync(player, cancellationToken), () => _soap.PreviousAsync(player, cancellationToken));

    /// <inheritdoc />
    public Task SeekAsync(DiscoveredPlayer player, TimeSpan position, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.SeekAsync(player, position, cancellationToken), () => _soap.SeekAsync(player, position, cancellationToken));

    /// <inheritdoc />
    public Task SetVolumeAsync(DiscoveredPlayer player, int volume, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.SetVolumeAsync(player, volume, cancellationToken), () => _soap.SetVolumeAsync(player, volume, cancellationToken));

    /// <inheritdoc />
    public Task SetMuteAsync(DiscoveredPlayer player, bool muted, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.SetMuteAsync(player, muted, cancellationToken), () => _soap.SetMuteAsync(player, muted, cancellationToken));

    /// <inheritdoc />
    public Task<(int Volume, bool Muted)> GetVolumeAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.GetVolumeAsync(player, cancellationToken), () => _soap.GetVolumeAsync(player, cancellationToken));

    /// <inheritdoc />
    public Task<TransportSnapshot> GetTransportAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
        => PreferLanAsync(player, () => _lan.GetTransportAsync(player, cancellationToken), () => _soap.GetTransportAsync(player, cancellationToken));

    /// <inheritdoc />
    public Task LoadCloudQueueAsync(DiscoveredPlayer player, LoadCloudQueueRequest request, CancellationToken cancellationToken)
        => _lan.LoadCloudQueueAsync(player, request, cancellationToken);

    /// <inheritdoc />
    public Task RefreshCloudQueueAsync(DiscoveredPlayer player, CancellationToken cancellationToken)
    {
        if (!_lan.HasSession(player.Id))
        {
            return Task.CompletedTask;
        }

        return _lan.RefreshCloudQueueAsync(player, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetPlayModesAsync(DiscoveredPlayer player, string repeat, bool shuffle, bool crossfade, CancellationToken cancellationToken)
        => PreferLanAsync(
            player,
            () => _lan.SetPlayModesAsync(player, repeat, shuffle, crossfade, cancellationToken),
            () => _soap.SetPlayModesAsync(player, repeat, shuffle, crossfade, cancellationToken));

    /// <inheritdoc />
    public Task SkipToItemAsync(DiscoveredPlayer player, string itemId, int positionMillis, CancellationToken cancellationToken)
        => _lan.SkipToItemAsync(player, itemId, positionMillis, cancellationToken);

    /// <inheritdoc />
    public Task<GroupCommandResult> CreateGroupAsync(
        DiscoveredPlayer player,
        IReadOnlyList<string> playerIds,
        string? musicContextGroupId,
        CancellationToken cancellationToken)
        => _lan.CreateGroupAsync(player, playerIds, musicContextGroupId, cancellationToken);

    /// <inheritdoc />
    public Task<GroupCommandResult> ModifyGroupMembersAsync(
        DiscoveredPlayer player,
        string groupId,
        IReadOnlyList<string> playerIdsToAdd,
        IReadOnlyList<string> playerIdsToRemove,
        CancellationToken cancellationToken)
        => _lan.ModifyGroupMembersAsync(player, groupId, playerIdsToAdd, playerIdsToRemove, cancellationToken);

    private async Task PreferLanAsync(DiscoveredPlayer player, Func<Task> lan, Func<Task> soap)
    {
        if (_lan.HasSession(player.Id))
        {
            try
            {
                await lan().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not SonosControlException { ErrorCode: "LanAuthRequired" })
            {
                _logger.LogDebug(ex, "LAN Control command failed; falling back to SOAP for {Player}", player.Id);
            }
        }

        await soap().ConfigureAwait(false);
    }

    private async Task<T> PreferLanAsync<T>(DiscoveredPlayer player, Func<Task<T>> lan, Func<Task<T>> soap)
    {
        if (_lan.HasSession(player.Id))
        {
            try
            {
                return await lan().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not SonosControlException { ErrorCode: "LanAuthRequired" })
            {
                _logger.LogDebug(ex, "LAN Control command failed; falling back to SOAP for {Player}", player.Id);
            }
        }

        return await soap().ConfigureAwait(false);
    }
}
