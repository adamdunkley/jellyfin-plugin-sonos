using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Api.Models;

/// <summary>
/// Cheap poll snapshot for <c>GET /Sonos/Queue</c>.
/// </summary>
public sealed class QueueResponse
{
    /// <summary>Gets the coordinator id.</summary>
    public string CoordinatorId { get; init; } = string.Empty;

    /// <summary>Gets playback state.</summary>
    public PlaybackState State { get; init; }

    /// <summary>Gets repeat mode.</summary>
    public string Repeat { get; init; } = "None";

    /// <summary>Gets a value indicating whether shuffle is on.</summary>
    public bool Shuffle { get; init; }

    /// <summary>Gets a value indicating whether crossfade is on.</summary>
    public bool Crossfade { get; init; }

    /// <summary>Gets volume 0-100.</summary>
    public int Volume { get; init; }

    /// <summary>Gets a value indicating whether output is muted.</summary>
    public bool Muted { get; init; }

    /// <summary>Gets position in the current track.</summary>
    public long PositionTicks { get; init; }

    /// <summary>Gets the current item index.</summary>
    public int CurrentIndex { get; init; }

    /// <summary>Gets queueVersion for Cloud Queue.</summary>
    public string QueueVersion { get; init; } = "0";

    /// <summary>Gets the Jellyfin user who started this queue.</summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the plugin currently owns the speaker transport.
    /// </summary>
    public bool PluginOwned { get; init; }

    /// <summary>Gets queue items.</summary>
    public IReadOnlyList<QueueItemDto> Items { get; init; } = [];
}
