using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Sonos.Api.Models;

namespace Jellyfin.Plugin.Sonos.Queue;

/// <summary>
/// Plugin-owned queue for one coordinator.
/// </summary>
public sealed class LogicalQueue
{
    /// <summary>Gets or sets the coordinator id.</summary>
    public string CoordinatorId { get; set; } = string.Empty;

    /// <summary>Gets the items.</summary>
    public List<LogicalQueueItem> Items { get; } = [];

    /// <summary>Gets or sets the playing index.</summary>
    public int CurrentIndex { get; set; }

    /// <summary>Gets or sets playback state.</summary>
    public PlaybackState State { get; set; } = PlaybackState.Stopped;

    /// <summary>Gets or sets position ticks.</summary>
    public long PositionTicks { get; set; }

    /// <summary>Gets or sets volume.</summary>
    public int Volume { get; set; }

    /// <summary>Gets or sets mute.</summary>
    public bool Muted { get; set; }

    /// <summary>Gets or sets repeat.</summary>
    public string Repeat { get; set; } = "None";

    /// <summary>Gets or sets shuffle.</summary>
    public bool Shuffle { get; set; }

    /// <summary>Gets or sets crossfade.</summary>
    public bool Crossfade { get; set; }

    /// <summary>Gets or sets queueVersion (bumped on every rewrite).</summary>
    public string QueueVersion { get; set; } = "0";

    /// <summary>Gets or sets context version.</summary>
    public string ContextVersion { get; set; } = "1";

    /// <summary>Gets or sets the Jellyfin user used to mint tokens.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets last transport poll UTC.</summary>
    public DateTimeOffset LastPoll { get; set; }

    /// <summary>Gets or sets the container display name.</summary>
    public string ContainerName { get; set; } = "Jellyfin";

    /// <summary>Gets or sets a value indicating whether LAN Cloud Queue is loaded.</summary>
    public bool UsesCloudQueue { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this plugin currently owns the speaker transport.
    /// True after a successful load (Cloud Queue or SOAP). False after Stop.
    /// </summary>
    public bool PluginOwned { get; set; }

    /// <summary>Bumps queueVersion.</summary>
    public void BumpVersion()
    {
        var next = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (string.Equals(next, QueueVersion, StringComparison.Ordinal)
            && long.TryParse(QueueVersion, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var current))
        {
            QueueVersion = (current + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        QueueVersion = next;
    }
}
