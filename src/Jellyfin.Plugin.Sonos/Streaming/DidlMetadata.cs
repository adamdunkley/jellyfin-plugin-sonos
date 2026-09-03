using System;
using System.Globalization;
using System.Net;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// DIDL-Lite metadata for SOAP SetAVTransportURI.
/// </summary>
public static class DidlMetadata
{
    /// <summary>
    /// Builds DIDL for a track URI.
    /// </summary>
    /// <param name="uri">Stream URI.</param>
    /// <param name="title">Title.</param>
    /// <param name="artist">Artist.</param>
    /// <param name="album">Album.</param>
    /// <param name="contentType">MIME type.</param>
    /// <param name="durationTicks">Duration.</param>
    /// <returns>DIDL XML.</returns>
    public static string ForTrack(string uri, string title, string artist, string album, string contentType, long durationTicks)
    {
        var duration = TimeSpan.FromTicks(durationTicks);
        var rel = string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
        var protocol = contentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ? "http-get:*:audio/mpeg:*"
            : contentType.Contains("flac", StringComparison.OrdinalIgnoreCase) ? "http-get:*:audio/flac:*"
            : "http-get:*:" + contentType + ":*";
        return
            "<DIDL-Lite xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\" xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\">" +
            "<item id=\"0\" parentID=\"0\" restricted=\"1\">" +
            "<dc:title>" + WebUtility.HtmlEncode(title) + "</dc:title>" +
            "<upnp:class>object.item.audioItem.musicTrack</upnp:class>" +
            "<dc:creator>" + WebUtility.HtmlEncode(artist) + "</dc:creator>" +
            "<upnp:artist>" + WebUtility.HtmlEncode(artist) + "</upnp:artist>" +
            "<upnp:album>" + WebUtility.HtmlEncode(album) + "</upnp:album>" +
            "<res protocolInfo=\"" + protocol + "\" duration=\"" + rel + "\">" + WebUtility.HtmlEncode(uri) + "</res>" +
            "</item></DIDL-Lite>";
    }
}
