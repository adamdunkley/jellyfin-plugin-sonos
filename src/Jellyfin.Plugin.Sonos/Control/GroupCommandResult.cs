using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Result of a LAN or SOAP grouping command.
/// </summary>
public sealed class GroupCommandResult
{
    /// <summary>Gets the Sonos group id.</summary>
    public string GroupId { get; init; } = string.Empty;

    /// <summary>Gets the coordinator player id.</summary>
    public string CoordinatorId { get; init; } = string.Empty;

    /// <summary>Gets member player ids returned by the speaker.</summary>
    public IReadOnlyList<string> PlayerIds { get; init; } = [];

    /// <summary>
    /// Parses a groups create/modify response.
    /// </summary>
    /// <param name="data">LAN JSON body.</param>
    /// <param name="fallbackCoordinatorId">Coordinator if omitted.</param>
    /// <param name="fallbackGroupId">Group id if omitted.</param>
    /// <returns>The result.</returns>
    public static GroupCommandResult FromLan(JsonNode? data, string fallbackCoordinatorId, string fallbackGroupId)
    {
        var obj = data as JsonObject;
        var group = obj?["group"] as JsonObject ?? obj;
        var groupId = group?["id"]?.GetValue<string>()
                      ?? group?["groupId"]?.GetValue<string>()
                      ?? fallbackGroupId;
        var coordinatorId = group?["coordinatorId"]?.GetValue<string>() ?? fallbackCoordinatorId;
        var playerIds = new List<string>();
        if (group?["playerIds"] is JsonArray array)
        {
            foreach (var node in array)
            {
                var id = node?.GetValue<string>();
                if (!string.IsNullOrEmpty(id))
                {
                    playerIds.Add(id);
                }
            }
        }

        if (playerIds.Count == 0)
        {
            playerIds.Add(coordinatorId);
        }

        return new GroupCommandResult
        {
            GroupId = groupId,
            CoordinatorId = coordinatorId,
            PlayerIds = playerIds
        };
    }
}
