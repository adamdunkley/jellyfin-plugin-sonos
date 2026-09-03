using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Sonos.Web;

/// <summary>
/// Serves the injected jellyfin-web client (JS/CSS) from embedded resources.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("Sonos/web")]
public class WebAssetsController : ControllerBase
{
    /// <summary>Handoff planner used by the injected client.</summary>
    /// <returns>The script.</returns>
    [HttpGet("player-handoff.js")]
    [Produces("application/javascript")]
    public IActionResult GetHandoff() => Embedded("player-handoff.js", "application/javascript; charset=utf-8");

    /// <summary>JavaScript client.</summary>
    /// <returns>The script.</returns>
    [HttpGet("sonos-client.js")]
    [Produces("application/javascript")]
    public IActionResult GetJavaScript() => Embedded("sonos-client.js", "application/javascript; charset=utf-8");

    /// <summary>Stylesheet.</summary>
    /// <returns>The CSS.</returns>
    [HttpGet("sonos-client.css")]
    [Produces("text/css")]
    public IActionResult GetCss() => Embedded("sonos-client.css", "text/css; charset=utf-8");

    private IActionResult Embedded(string fileName, string contentType)
    {
        var name = typeof(Plugin).Namespace + ".Web." + fileName;
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=300";
        return File(stream, contentType);
    }
}
