using Jellyfin.Plugin.Sonos.Api;
using Jellyfin.Plugin.Sonos.Discovery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Resolves a client targetId (player or group) to the group coordinator.
/// </summary>
public sealed class TargetResolver
{
    private readonly PlayerRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="TargetResolver"/> class.
    /// </summary>
    /// <param name="registry">Player registry.</param>
    public TargetResolver(PlayerRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Resolves a target to a live coordinator.
    /// </summary>
    /// <param name="targetId">Player or group id.</param>
    /// <param name="coordinator">Coordinator when successful.</param>
    /// <param name="error">Problem result when unsuccessful.</param>
    /// <returns>True when resolved and available.</returns>
    public bool TryResolve(string? targetId, out DiscoveredPlayer coordinator, out ObjectResult? error)
    {
        coordinator = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            error = ProblemResults.Create(StatusCodes.Status400BadRequest, "InvalidTarget", "targetId is required");
            return false;
        }

        if (!_registry.TryGetCoordinator(targetId, out coordinator))
        {
            error = ProblemResults.Create(StatusCodes.Status404NotFound, "PlayerNotFound", "No player or group matched targetId");
            return false;
        }

        if (!coordinator.Available)
        {
            error = ProblemResults.Create(StatusCodes.Status409Conflict, "PlayerUnavailable", coordinator.Name + " is offline");
            return false;
        }

        return true;
    }
}
