using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Reads audio stream facts from a library item.
/// </summary>
public static class LibraryAudioProbe
{
    /// <summary>
    /// Builds an <see cref="AudioStreamInfo"/> from a Jellyfin item.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <param name="streams">Media streams for the item.</param>
    /// <returns>Probe result.</returns>
    public static AudioStreamInfo FromItem(BaseItem item, IReadOnlyList<MediaStream>? streams)
    {
        var stream = streams?.FirstOrDefault(s => s.Type == MediaStreamType.Audio);
        var container = string.Empty;
        if (!string.IsNullOrEmpty(item.Path))
        {
            container = Path.GetExtension(item.Path).TrimStart('.');
        }

        return new AudioStreamInfo
        {
            Codec = stream?.Codec ?? container ?? string.Empty,
            Container = container ?? string.Empty,
            SampleRate = stream?.SampleRate ?? 0,
            BitDepth = stream?.BitDepth ?? 0,
            Channels = stream?.Channels ?? 2
        };
    }
}
