using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Sonos.Configuration;

/// <summary>
/// Plugin configuration persisted by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether discovery and playback are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the Jellyfin user id used for unattended Play To and as a fallback.
    /// Client-authenticated queue commands use the calling user instead.
    /// </summary>
    public string DefaultUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL speakers use to reach this server, e.g.
    /// <c>http://192.0.2.10:8096/media</c>. Must be routable from the speaker, not localhost. Include a base path if the server uses one.
    /// </summary>
    public string PublishedBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional comma-separated player IPs used when multicast discovery fails.
    /// </summary>
    public string SeedPlayerIps { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the preferred transcode codec.
    /// </summary>
    public TranscodeCodec PreferredTranscodeCodec { get; set; } = TranscodeCodec.Flac;

    /// <summary>
    /// Gets or sets comma-separated RINCON ids that should never be exposed or controlled.
    /// </summary>
    public string IgnoredPlayerIds { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether verbose Sonos protocol logging is enabled.
    /// </summary>
    public bool VerboseProtocolLogging { get; set; }
}
