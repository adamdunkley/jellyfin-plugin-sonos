using System.Collections.Generic;

namespace Jellyfin.Plugin.Sonos.Queue;

/// <summary>
/// A Cloud Queue itemWindow projection.
/// </summary>
public sealed class CloudQueueWindow
{
    /// <summary>Gets the window items.</summary>
    public IReadOnlyList<LogicalQueueItem> Items { get; init; } = [];

    /// <summary>Gets a value indicating whether the window includes index 0.</summary>
    public bool IncludesBeginningOfQueue { get; init; }

    /// <summary>Gets a value indicating whether the window includes the last item.</summary>
    public bool IncludesEndOfQueue { get; init; }
}
