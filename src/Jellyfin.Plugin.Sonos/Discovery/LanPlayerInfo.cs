namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// Subset of GET /api/v1/players/local/info.
/// </summary>
public sealed class LanPlayerInfo
{
    /// <summary>Gets or sets the player id.</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the household id.</summary>
    public string HouseholdId { get; set; } = string.Empty;

    /// <summary>Gets or sets the websocket URL.</summary>
    public string WebsocketUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the group id.</summary>
    public string GroupId { get; set; } = string.Empty;
}
