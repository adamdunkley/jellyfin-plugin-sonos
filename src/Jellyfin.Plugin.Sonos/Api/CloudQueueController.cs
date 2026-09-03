using System.Text.Json.Nodes;
using Jellyfin.Plugin.Sonos.Discovery;
using Jellyfin.Plugin.Sonos.Queue;
using Jellyfin.Plugin.Sonos.Util;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Api;

/// <summary>
/// Speaker-facing Cloud Queue (context / itemWindow / version).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("Sonos/queue/{playerOrGroupId}/v2.3")]
public class CloudQueueController : ControllerBase
{
    private readonly PlayerRegistry _registry;
    private readonly LogicalQueueStore _queues;
    private readonly ILogger<CloudQueueController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudQueueController"/> class.
    /// </summary>
    /// <param name="registry">Players.</param>
    /// <param name="queues">Logical queues.</param>
    /// <param name="logger">Logger.</param>
    public CloudQueueController(PlayerRegistry registry, LogicalQueueStore queues, ILogger<CloudQueueController> logger)
    {
        _registry = registry;
        _queues = queues;
        _logger = logger;
    }

    /// <summary>Cloud Queue context.</summary>
    /// <param name="playerOrGroupId">Coordinator or group id.</param>
    /// <returns>Context JSON.</returns>
    [HttpGet("context")]
    public IActionResult GetContext(string playerOrGroupId)
    {
        if (!TryGetQueue(playerOrGroupId, out var queue, out var error))
        {
            return error!;
        }

        if (!TryPublished(out var published, out var publishedError))
        {
            return publishedError!;
        }

        lock (queue)
        {
            Log("context", playerOrGroupId, queue.Items.Count);
            return JsonContent(CloudQueueJson.Context(queue, published));
        }
    }

    /// <summary>Cloud Queue item window.</summary>
    /// <param name="playerOrGroupId">Coordinator or group id.</param>
    /// <param name="itemId">Center item, or empty for the start.</param>
    /// <param name="previousWindowSize">Items before center.</param>
    /// <param name="upcomingWindowSize">Items after center.</param>
    /// <param name="reason">Sonos reason parameter.</param>
    /// <returns>Window JSON.</returns>
    [HttpGet("itemWindow")]
    public IActionResult GetItemWindow(
        string playerOrGroupId,
        [FromQuery] string? itemId,
        [FromQuery] int previousWindowSize = 10,
        [FromQuery] int upcomingWindowSize = 10,
        [FromQuery] string? reason = null)
    {
        if (!TryGetQueue(playerOrGroupId, out var queue, out var error))
        {
            return error!;
        }

        if (!TryPublished(out var published, out var publishedError))
        {
            return publishedError!;
        }

        lock (queue)
        {
            var window = CloudQueueWindowBuilder.Slice(queue.Items, itemId, previousWindowSize, upcomingWindowSize);
            Log("itemWindow", playerOrGroupId, window.Items.Count, reason, itemId);
            return JsonContent(CloudQueueJson.ItemWindow(queue, published, window));
        }
    }

    /// <summary>Cloud Queue version poll.</summary>
    /// <param name="playerOrGroupId">Coordinator or group id.</param>
    /// <returns>Versions.</returns>
    [HttpGet("version")]
    public IActionResult GetVersion(string playerOrGroupId)
    {
        if (!TryGetQueue(playerOrGroupId, out var queue, out var error))
        {
            return error!;
        }

        lock (queue)
        {
            return JsonContent(new JsonObject
            {
                ["contextVersion"] = queue.ContextVersion,
                ["queueVersion"] = queue.QueueVersion
            });
        }
    }

    /// <summary>Playback progress reports from the speaker. Accepted and ignored.</summary>
    /// <returns>No content.</returns>
    [HttpPost("timePlayed")]
    [HttpPut("timePlayed")]
    public IActionResult TimePlayed() => new StatusCodeResult(StatusCodes.Status204NoContent);

    private bool TryGetQueue(string playerOrGroupId, out LogicalQueue queue, out ObjectResult? error)
    {
        error = null;
        queue = null!;
        if (!_registry.TryGetCoordinator(playerOrGroupId, out var coordinator)
            && !_registry.TryGet(playerOrGroupId, out coordinator))
        {
            error = ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No player or group matched id");
            return false;
        }

        queue = _queues.GetOrCreate(coordinator.Id);
        return true;
    }

    private static bool TryPublished(out string published, out ObjectResult? error)
    {
        error = null;
        if (PublishedUrlGuard.TryValidate(Plugin.Instance?.Configuration.PublishedBaseUrl, out published, out _))
        {
            return true;
        }

        published = string.Empty;
        error = ProblemResults.Create(StatusCodes.Status400BadRequest, "PublishedUrlInvalid", "Published base URL is not set");
        return false;
    }

    private static ContentResult JsonContent(JsonObject payload)
        => new()
        {
            Content = payload.ToJsonString(),
            ContentType = "application/json",
            StatusCode = StatusCodes.Status200OK
        };

    private void Log(string endpoint, string player, int itemCount, string? reason = null, string? itemId = null)
    {
        if (Plugin.Instance?.Configuration.VerboseProtocolLogging != true)
        {
            return;
        }

        _logger.LogInformation(
            "Cloud Queue {Endpoint} player={Player} items={Count} reason={Reason} itemId={ItemId}",
            endpoint,
            player,
            itemCount,
            reason ?? string.Empty,
            string.IsNullOrEmpty(itemId) ? "(start)" : itemId);
    }
}
