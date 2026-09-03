using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Web;

/// <summary>
/// Registers <see cref="SonosIndexHtmlMiddleware"/> at the start of the pipeline.
/// </summary>
public sealed class SonosWebInjectionStartupFilter : IStartupFilter
{
    private readonly ILogger<SonosWebInjectionStartupFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosWebInjectionStartupFilter"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public SonosWebInjectionStartupFilter(ILogger<SonosWebInjectionStartupFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        _logger.LogInformation("Registering Sonos index.html injection middleware");
        return app =>
        {
            app.UseMiddleware<SonosIndexHtmlMiddleware>();
            next(app);
        };
    }
}
