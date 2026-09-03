using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Sonos.Api;

/// <summary>
/// Client-facing Sonos API. Authenticated Jellyfin clients call these endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("Sonos")]
public class SonosApiController : ControllerBase
{
    private readonly PlayerRegistry _registry;
    private readonly SonosPlaybackService _playback;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosApiController"/> class.
    /// </summary>
    /// <param name="registry">Discovered players and groups.</param>
    /// <param name="playback">Playback orchestration.</param>
    public SonosApiController(PlayerRegistry registry, SonosPlaybackService playback)
    {
        _registry = registry;
        _playback = playback;
    }

    /// <summary>
    /// Gets discovered players and current Sonos groups.
    /// </summary>
    /// <returns>Players and groups.</returns>
    [HttpGet("Players")]
    [ProducesResponseType(typeof(PlayersResponse), StatusCodes.Status200OK)]
    public ActionResult<PlayersResponse> GetPlayers()
    {
        return Ok(_registry.GetSnapshot());
    }

    /// <summary>
    /// Gets one player, including live volume when cheap.
    /// </summary>
    /// <param name="id">Player id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The player.</returns>
    [HttpGet("Players/{id}")]
    [ProducesResponseType(typeof(PlayerInfo), StatusCodes.Status200OK)]
    public Task<ActionResult> GetPlayerAsync(string id, CancellationToken cancellationToken)
        => _playback.GetPlayerAsync(id, cancellationToken);

    /// <summary>
    /// Replaces the logical queue and starts playback.
    /// </summary>
    /// <param name="request">Play body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot.</returns>
    [HttpPost("Queue/Play")]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> PlayAsync([FromBody] PlayQueueRequest request, CancellationToken cancellationToken)
    {
        var userId = _playback.ResolveUserId(User, out var error);
        if (userId is null)
        {
            return error!;
        }

        return await _playback.PlayAsync(request, userId.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds items to the logical queue.
    /// </summary>
    /// <param name="request">Add body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot.</returns>
    [HttpPost("Queue/Add")]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult> AddAsync([FromBody] AddQueueRequest request, CancellationToken cancellationToken)
    {
        var userId = _playback.ResolveUserId(User, out var error);
        if (userId is null)
        {
            return error!;
        }

        return await _playback.AddAsync(request, userId.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes items from the logical queue.
    /// </summary>
    /// <param name="request">Remove body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot.</returns>
    [HttpPost("Queue/Remove")]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    public Task<ActionResult> RemoveAsync([FromBody] RemoveQueueRequest request, CancellationToken cancellationToken)
        => _playback.RemoveAsync(request, cancellationToken);

    /// <summary>
    /// Moves a queue item.
    /// </summary>
    /// <param name="request">Move body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot.</returns>
    [HttpPost("Queue/Move")]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    public Task<ActionResult> MoveAsync([FromBody] MoveQueueRequest request, CancellationToken cancellationToken)
        => _playback.MoveAsync(request, cancellationToken);

    /// <summary>
    /// Cheap queue poll (~1–2 Hz).
    /// </summary>
    /// <param name="targetId">Player or group id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot.</returns>
    [HttpGet("Queue")]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    public Task<ActionResult> GetQueueAsync([FromQuery] string targetId, CancellationToken cancellationToken)
        => _playback.GetQueueAsync(targetId, cancellationToken);

    /// <summary>
    /// Transport and grouping-local commands (volume, seek, skip, …).
    /// </summary>
    /// <param name="request">Command body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue snapshot.</returns>
    [HttpPost("Playstate")]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    public Task<ActionResult> PlaystateAsync([FromBody] PlaystateRequest request, CancellationToken cancellationToken)
        => _playback.PlaystateAsync(request, cancellationToken);

    /// <summary>
    /// Lists current Sonos groups.
    /// </summary>
    /// <returns>Groups.</returns>
    [HttpGet("Groups")]
    [ProducesResponseType(typeof(GroupsResponse), StatusCodes.Status200OK)]
    public ActionResult GetGroups() => _playback.GetGroups();

    /// <summary>
    /// Groups the listed players. Bonded satellites follow their coordinator.
    /// </summary>
    /// <param name="request">Player ids and optional coordinator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated groups.</returns>
    [HttpPost("Groups")]
    [ProducesResponseType(typeof(GroupsResponse), StatusCodes.Status200OK)]
    public Task<ActionResult> CreateGroupAsync([FromBody] CreateGroupRequest request, CancellationToken cancellationToken)
        => _playback.CreateGroupAsync(request, cancellationToken);

    /// <summary>
    /// Adds or removes players on an existing group.
    /// </summary>
    /// <param name="id">Group id.</param>
    /// <param name="request">Members to add or remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated groups.</returns>
    [HttpPost("Groups/{id}/Members")]
    [ProducesResponseType(typeof(GroupsResponse), StatusCodes.Status200OK)]
    public Task<ActionResult> ModifyGroupMembersAsync(
        string id,
        [FromBody] ModifyGroupMembersRequest request,
        CancellationToken cancellationToken)
        => _playback.ModifyGroupMembersAsync(id, request, cancellationToken);
}
