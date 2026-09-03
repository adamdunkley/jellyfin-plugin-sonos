using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Sonos.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Sonos;

/// <summary>
/// The Sonos plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Plugin identifier. Must stay stable across versions.
    /// </summary>
    public static readonly Guid PluginGuid = Guid.Parse("cef190c1-177d-4018-8271-7a3aa6033a3f");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Sonos";

    /// <inheritdoc />
    public override string Description => "Play Jellyfin music to Sonos S2 speakers using native queueing.";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    ns)
            },
            new PluginPageInfo
            {
                Name = "player-handoff.js",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Web.player-handoff.js",
                    ns)
            },
            new PluginPageInfo
            {
                Name = "sonos-client.js",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Web.sonos-client.js",
                    ns)
            },
            new PluginPageInfo
            {
                Name = "sonos-client.css",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Web.sonos-client.css",
                    ns)
            }
        ];
    }
}
