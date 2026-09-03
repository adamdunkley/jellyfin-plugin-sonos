using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Web;

/// <summary>
/// Buffers jellyfin-web index.html and injects the Sonos client assets.
/// </summary>
public sealed class SonosIndexHtmlMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SonosIndexHtmlMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosIndexHtmlMiddleware"/> class.
    /// </summary>
    /// <param name="next">Next middleware.</param>
    /// <param name="logger">Logger.</param>
    public SonosIndexHtmlMiddleware(RequestDelegate next, ILogger<SonosIndexHtmlMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (!IndexHtmlInjector.IsWebIndexPath(path)
            || (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Request.Headers.AcceptEncoding = string.Empty;
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");

        var originalFeature = context.Features.Get<IHttpResponseBodyFeature>();
        using var buffer = new MemoryStream();
        var bufferFeature = new StreamResponseBodyFeature(buffer);
        context.Features.Set<IHttpResponseBodyFeature>(bufferFeature);
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
            await bufferFeature.CompleteAsync().ConfigureAwait(false);

            if (context.Response.StatusCode is < 200 or >= 300)
            {
                await WriteOriginalAsync(originalFeature, buffer, context).ConfigureAwait(false);
                return;
            }

            var contentType = context.Response.ContentType ?? string.Empty;
            if (contentType.Length > 0
                && contentType.IndexOf("html", StringComparison.OrdinalIgnoreCase) < 0)
            {
                await WriteOriginalAsync(originalFeature, buffer, context).ConfigureAwait(false);
                return;
            }

            buffer.Seek(0, SeekOrigin.Begin);
            var html = Encoding.UTF8.GetString(buffer.ToArray());
            var publicBase = IndexHtmlInjector.ResolvePublicBase(context.Request.PathBase.Value, path);
            var version = Uri.EscapeDataString(Plugin.Instance?.Version.ToString() ?? "0");
            var cssUrl = publicBase + "/Sonos/web/sonos-client.css?v=" + version;
            var handoffUrl = publicBase + "/Sonos/web/player-handoff.js?v=" + version;
            var scriptUrl = publicBase + "/Sonos/web/sonos-client.js?v=" + version;
            var transformed = IndexHtmlInjector.Inject(html, cssUrl, handoffUrl, scriptUrl);
            var bytes = Encoding.UTF8.GetBytes(transformed);
            context.Response.ContentLength = bytes.Length;
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.CacheControl = "no-store";
            if (originalFeature is not null)
            {
                await originalFeature.Stream.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
            }

            _logger.LogInformation("Injected Sonos web client into {Path}", path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to inject Sonos web client into index.html");
            await WriteOriginalAsync(originalFeature, buffer, context).ConfigureAwait(false);
        }
        finally
        {
            (bufferFeature as IDisposable)?.Dispose();
            if (originalFeature is not null)
            {
                context.Features.Set(originalFeature);
                context.Response.Body = originalFeature.Stream;
            }
        }
    }

    private static async Task WriteOriginalAsync(
        IHttpResponseBodyFeature? originalFeature,
        MemoryStream buffer,
        HttpContext context)
    {
        if (originalFeature is null || buffer.Length == 0)
        {
            return;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        await buffer.CopyToAsync(originalFeature.Stream, context.RequestAborted).ConfigureAwait(false);
    }
}
