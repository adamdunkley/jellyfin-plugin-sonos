using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Speaker-facing tokenized audio. Speakers do not send Jellyfin auth headers.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("Sonos/stream")]
public class StreamController : ControllerBase
{
    private readonly StreamTokenService _tokens;
    private readonly ILibraryManager _libraryManager;
    private readonly FfmpegTranscodeCache _cache;
    private readonly ILogger<StreamController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamController"/> class.
    /// </summary>
    /// <param name="tokens">Token service.</param>
    /// <param name="libraryManager">Library.</param>
    /// <param name="cache">Transcode cache.</param>
    /// <param name="logger">Logger.</param>
    public StreamController(
        StreamTokenService tokens,
        ILibraryManager libraryManager,
        FfmpegTranscodeCache cache,
        ILogger<StreamController> logger)
    {
        _tokens = tokens;
        _libraryManager = libraryManager;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>HEAD probe.</summary>
    /// <param name="token">Stream token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Headers only.</returns>
    [HttpHead("{token}")]
    public Task<IActionResult> HeadAsync(string token, CancellationToken cancellationToken)
        => ServeAsync(token, head: true, cancellationToken);

    /// <summary>GET audio with Range.</summary>
    /// <param name="token">Stream token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audio stream.</returns>
    [HttpGet("{token}")]
    public Task<IActionResult> GetAsync(string token, CancellationToken cancellationToken)
        => ServeAsync(token, head: false, cancellationToken);

    private async Task<IActionResult> ServeAsync(string token, bool head, CancellationToken cancellationToken)
    {
        if (!_tokens.TryUnpack(token, out var payload, out var expired))
        {
            return ProblemResults.Create(StatusCodes.Status403Forbidden, "InvalidToken", "Stream token is invalid");
        }

        if (expired)
        {
            return ProblemResults.Create(StatusCodes.Status410Gone, "StreamExpired", "Stream token expired");
        }

        var item = _libraryManager.GetItemById<BaseItem>(payload.ItemId, payload.UserId);
        if (item is null || string.IsNullOrEmpty(item.Path) || !System.IO.File.Exists(item.Path))
        {
            return ProblemResults.Create(StatusCodes.Status404NotFound, "ItemNotFound", "Audio item is not available");
        }

        string path;
        string contentType;
        if (payload.DirectPlay)
        {
            path = item.Path;
            contentType = ContentTypeFor(payload.Container);
        }
        else
        {
            var mtime = System.IO.File.GetLastWriteTimeUtc(item.Path).Ticks;
            var decision = new TranscodeDecision
            {
                DirectPlay = false,
                Container = payload.Container,
                SampleRate = payload.SampleRate,
                BitDepth = payload.BitDepth,
                Reason = TranscodeReason.CodecNotSupported
            };
            path = await _cache.GetOrCreateAsync(item.Path, payload.ItemId, mtime, decision, cancellationToken).ConfigureAwait(false);
            contentType = decision.ContentType;
        }

        _logger.LogDebug("Serving Sonos stream for item {ItemId} ({Mode})", payload.ItemId, payload.DirectPlay ? "copy" : "transcode");

        if (head)
        {
            var length = new FileInfo(path).Length;
            Response.ContentType = contentType;
            Response.ContentLength = length;
            Response.Headers.AcceptRanges = "bytes";
            return new EmptyResult();
        }

        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    private static string ContentTypeFor(string container) => container switch
    {
        "mp3" => "audio/mpeg",
        "aac" or "m4a" or "mp4" => "audio/mp4",
        "ogg" => "audio/ogg",
        _ => "audio/flac"
    };
}
