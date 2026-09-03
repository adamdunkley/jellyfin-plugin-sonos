using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Discovery;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Control plane for a Sonos coordinator. LAN Control with SOAP fallback.
/// </summary>
public interface ISonosControlClient
{
    /// <summary>Sets the current transport URI (SOAP / single-track path).</summary>
    Task SetAvTransportUriAsync(DiscoveredPlayer player, string uri, string metadataXml, CancellationToken cancellationToken);

    /// <summary>Starts playback.</summary>
    Task PlayAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Pauses playback.</summary>
    Task PauseAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Stops playback.</summary>
    Task StopAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Skips to next track (Cloud Queue / native).</summary>
    Task NextAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Skips to previous track.</summary>
    Task PreviousAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Seeks within the current track.</summary>
    Task SeekAsync(DiscoveredPlayer player, TimeSpan position, CancellationToken cancellationToken);

    /// <summary>Sets group volume 0-100.</summary>
    Task SetVolumeAsync(DiscoveredPlayer player, int volume, CancellationToken cancellationToken);

    /// <summary>Sets mute.</summary>
    Task SetMuteAsync(DiscoveredPlayer player, bool muted, CancellationToken cancellationToken);

    /// <summary>Reads volume and mute.</summary>
    Task<(int Volume, bool Muted)> GetVolumeAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Reads transport state and position.</summary>
    Task<TransportSnapshot> GetTransportAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Loads a Cloud Queue on the coordinator (LAN Control).</summary>
    Task LoadCloudQueueAsync(DiscoveredPlayer player, LoadCloudQueueRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the player to refetch the Cloud Queue window.</summary>
    Task RefreshCloudQueueAsync(DiscoveredPlayer player, CancellationToken cancellationToken);

    /// <summary>Sets repeat / shuffle / crossfade play modes. Repeat is None, All, or One.</summary>
    Task SetPlayModesAsync(DiscoveredPlayer player, string repeat, bool shuffle, bool crossfade, CancellationToken cancellationToken);

    /// <summary>Skips to a Cloud Queue item id, optionally at an offset.</summary>
    Task SkipToItemAsync(DiscoveredPlayer player, string itemId, int positionMillis, CancellationToken cancellationToken);

    /// <summary>Creates a group from player ids (LAN Control createGroup).</summary>
    Task<GroupCommandResult> CreateGroupAsync(
        DiscoveredPlayer player,
        IReadOnlyList<string> playerIds,
        string? musicContextGroupId,
        CancellationToken cancellationToken);

    /// <summary>Adds or removes players from an existing group.</summary>
    Task<GroupCommandResult> ModifyGroupMembersAsync(
        DiscoveredPlayer player,
        string groupId,
        IReadOnlyList<string> playerIdsToAdd,
        IReadOnlyList<string> playerIdsToRemove,
        CancellationToken cancellationToken);
}
