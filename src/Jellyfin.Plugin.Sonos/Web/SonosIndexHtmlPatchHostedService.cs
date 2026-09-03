using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Web;

/// <summary>
/// Patches on-disk jellyfin-web index.html when request-time inject is skipped (SendFile / pipeline order).
/// </summary>
public sealed class SonosIndexHtmlPatchHostedService : IHostedService
{
    private readonly IApplicationPaths _paths;
    private readonly ILogger<SonosIndexHtmlPatchHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosIndexHtmlPatchHostedService"/> class.
    /// </summary>
    /// <param name="paths">Application paths (includes jellyfin-web).</param>
    /// <param name="logger">Logger.</param>
    public SonosIndexHtmlPatchHostedService(
        IApplicationPaths paths,
        ILogger<SonosIndexHtmlPatchHostedService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var index = Path.Combine(_paths.WebPath, "index.html");
            if (!File.Exists(index))
            {
                _logger.LogInformation("jellyfin-web index.html not found at {Path}", index);
                return;
            }

            var html = await File.ReadAllTextAsync(index, cancellationToken).ConfigureAwait(false);
            if (html.Contains(IndexHtmlInjector.Marker, StringComparison.Ordinal))
            {
                return;
            }

            var publicBase = PublicBaseFromPublishedUrl();
            var version = Uri.EscapeDataString(Plugin.Instance?.Version.ToString() ?? "0");
            var injected = IndexHtmlInjector.Inject(
                html,
                publicBase + "/Sonos/web/sonos-client.css?v=" + version,
                publicBase + "/Sonos/web/player-handoff.js?v=" + version,
                publicBase + "/Sonos/web/sonos-client.js?v=" + version);
            await File.WriteAllTextAsync(index, injected, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Patched jellyfin-web index.html at {Path}", index);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not patch jellyfin-web index.html on disk; request-time inject must succeed instead");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string PublicBaseFromPublishedUrl()
    {
        var published = Plugin.Instance?.Configuration.PublishedBaseUrl;
        if (Uri.TryCreate(published, UriKind.Absolute, out var uri))
        {
            return uri.AbsolutePath.TrimEnd('/');
        }

        return string.Empty;
    }
}
