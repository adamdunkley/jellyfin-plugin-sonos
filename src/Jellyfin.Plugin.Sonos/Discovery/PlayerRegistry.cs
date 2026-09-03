using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Sonos.Api.Models;

namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// In-memory registry of discovered Sonos players and groups.
/// </summary>
public sealed class PlayerRegistry
{
    private readonly ConcurrentDictionary<string, DiscoveredPlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<IReadOnlySet<string>> _ignoredIds;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerRegistry"/> class using plugin configuration.
    /// </summary>
    public PlayerRegistry()
        : this(ReadIgnoredIdsFromPlugin)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerRegistry"/> class.
    /// </summary>
    /// <param name="ignoredIds">Returns RINCON ids that must not be stored or exposed.</param>
    internal PlayerRegistry(Func<IReadOnlySet<string>> ignoredIds)
    {
        _ignoredIds = ignoredIds ?? throw new ArgumentNullException(nameof(ignoredIds));
    }

    /// <summary>
    /// Replaces or inserts a player record.
    /// </summary>
    /// <param name="player">The player to store.</param>
    public void Upsert(PlayerInfo player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Upsert(new DiscoveredPlayer
        {
            Id = player.Id,
            Name = player.Name,
            Model = player.Model,
            ModelDisplayName = player.ModelDisplayName,
            Ip = player.Ip,
            GroupId = player.GroupId,
            IsCoordinator = player.IsCoordinator,
            Available = player.Available,
            Volume = player.Volume,
            Muted = player.Muted,
            LastSeen = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Replaces or inserts a discovered player.
    /// </summary>
    /// <param name="player">The player to store.</param>
    public void Upsert(DiscoveredPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (string.IsNullOrWhiteSpace(player.Id) || _ignoredIds().Contains(player.Id))
        {
            return;
        }

        player.LastSeen = DateTimeOffset.UtcNow;
        player.MissedCycles = 0;
        player.Available = true;
        _players.AddOrUpdate(
            player.Id,
            player,
            (_, existing) =>
            {
                existing.Name = string.IsNullOrEmpty(player.Name) ? existing.Name : player.Name;
                existing.Model = string.IsNullOrEmpty(player.Model) ? existing.Model : player.Model;
                existing.ModelDisplayName = string.IsNullOrEmpty(player.ModelDisplayName) ? existing.ModelDisplayName : player.ModelDisplayName;
                existing.Ip = string.IsNullOrEmpty(player.Ip) ? existing.Ip : player.Ip;
                existing.GroupId = player.GroupId ?? existing.GroupId;
                existing.IsCoordinator = player.IsCoordinator;
                existing.Available = true;
                existing.HouseholdId = string.IsNullOrEmpty(player.HouseholdId) ? existing.HouseholdId : player.HouseholdId;
                existing.WebsocketUrl = string.IsNullOrEmpty(player.WebsocketUrl) ? existing.WebsocketUrl : player.WebsocketUrl;
                existing.LastSeen = DateTimeOffset.UtcNow;
                existing.MissedCycles = 0;
                if (player.Volume is not null)
                {
                    existing.Volume = player.Volume;
                }

                if (player.Muted is not null)
                {
                    existing.Muted = player.Muted;
                }

                return existing;
            });
    }

    /// <summary>
    /// Tries to get a discovered player.
    /// </summary>
    /// <param name="id">Player or group id.</param>
    /// <param name="player">The player.</param>
    /// <returns>True if found.</returns>
    public bool TryGet(string id, out DiscoveredPlayer player)
    {
        if (_players.TryGetValue(id, out player!))
        {
            return true;
        }

        player = _players.Values.FirstOrDefault(p =>
            p.IsCoordinator && string.Equals(p.GroupId, id, StringComparison.OrdinalIgnoreCase))!;
        return player is not null;
    }

    /// <summary>
    /// Resolves a player or group id to the group coordinator.
    /// </summary>
    /// <param name="targetId">Player or group id.</param>
    /// <param name="coordinator">Coordinator player.</param>
    /// <returns>True if resolved.</returns>
    public bool TryGetCoordinator(string targetId, out DiscoveredPlayer coordinator)
    {
        coordinator = null!;
        if (!TryGet(targetId, out var player))
        {
            return false;
        }

        if (player.IsCoordinator || string.IsNullOrEmpty(player.GroupId))
        {
            coordinator = player;
            return true;
        }

        var found = _players.Values.FirstOrDefault(p =>
            p.IsCoordinator && string.Equals(p.GroupId, player.GroupId, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            coordinator = player;
            return true;
        }

        coordinator = found;
        return true;
    }

    /// <summary>
    /// Marks players not seen this cycle as stale after three misses.
    /// </summary>
    /// <param name="seenIds">Ids observed this pass.</param>
    public void CompleteCycle(IReadOnlyCollection<string> seenIds)
    {
        var seen = new HashSet<string>(seenIds, StringComparer.OrdinalIgnoreCase);
        foreach (var player in _players.Values)
        {
            if (seen.Contains(player.Id))
            {
                continue;
            }

            player.MissedCycles++;
            if (player.MissedCycles >= 3)
            {
                player.Available = false;
            }
        }
    }

    /// <summary>
    /// Returns the current players and groups snapshot for the public API.
    /// </summary>
    /// <returns>A <see cref="PlayersResponse"/>.</returns>
    public PlayersResponse GetSnapshot()
    {
        var ignored = _ignoredIds();
        var players = _players.Values
            .Where(p => !ignored.Contains(p.Id))
            .Select(p => p.ToInfo())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var groups = players
            .Where(p => p.IsCoordinator && !string.IsNullOrEmpty(p.GroupId))
            .GroupBy(p => p.GroupId!, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var coordinator = g.First();
                var members = new[] { coordinator.Id }
                    .Concat(players
                        .Where(p => string.Equals(p.GroupId, g.Key, StringComparison.OrdinalIgnoreCase)
                                    && !string.Equals(p.Id, coordinator.Id, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(p => p.Id))
                    .ToArray();

                return new GroupInfo
                {
                    Id = g.Key,
                    Name = BuildGroupName(players, members),
                    CoordinatorId = coordinator.Id,
                    MemberIds = members,
                    PlaybackState = PlaybackState.Stopped
                };
            })
            .ToArray();

        return new PlayersResponse
        {
            Players = players,
            Groups = groups
        };
    }

    /// <summary>
    /// Updates group membership after a LAN grouping command. Each listed player
    /// brings along anyone already in its previous group (bonded satellites).
    /// </summary>
    /// <param name="groupId">New group id.</param>
    /// <param name="coordinatorId">Coordinator player id.</param>
    /// <param name="memberIds">Player ids included in the command or response.</param>
    public void ApplyGroupMembership(string groupId, string coordinatorId, IReadOnlyList<string> memberIds)
    {
        if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(coordinatorId))
        {
            return;
        }

        var brought = new HashSet<string>(memberIds ?? [], StringComparer.OrdinalIgnoreCase) { coordinatorId };
        var oldGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in _players.Values)
        {
            if (brought.Contains(player.Id) && !string.IsNullOrEmpty(player.GroupId))
            {
                oldGroupIds.Add(player.GroupId);
            }
        }

        foreach (var player in _players.Values)
        {
            var listed = brought.Contains(player.Id);
            var follower = !string.IsNullOrEmpty(player.GroupId) && oldGroupIds.Contains(player.GroupId);
            if (!listed && !follower)
            {
                continue;
            }

            player.GroupId = groupId;
            player.IsCoordinator = string.Equals(player.Id, coordinatorId, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Marks a player as a standalone coordinator after leaving a group.
    /// </summary>
    /// <param name="playerId">Player id.</param>
    public void ApplyStandalone(string playerId)
    {
        if (!_players.TryGetValue(playerId, out var player))
        {
            return;
        }

        player.GroupId = player.Id;
        player.IsCoordinator = true;
    }

    private static IReadOnlySet<string> ReadIgnoredIdsFromPlugin()
    {
        return ParseIgnoredIds(Plugin.Instance?.Configuration.IgnoredPlayerIds);
    }

    /// <summary>
    /// Parses a comma-separated ignored-id list.
    /// </summary>
    /// <param name="value">Raw config string.</param>
    /// <returns>A case-insensitive set of ids.</returns>
    public static IReadOnlySet<string> ParseIgnoredIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildGroupName(IReadOnlyList<PlayerInfo> players, IReadOnlyList<string> memberIds)
    {
        var names = memberIds
            .Select(id => players.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray();

        return names.Length == 0 ? "Group" : string.Join(" + ", names);
    }
}
