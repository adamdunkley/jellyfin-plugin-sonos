namespace Jellyfin.Plugin.Sonos;

/// <summary>
/// Shared Sonos LAN constants. Token matches aiosonos LOCAL_API_TOKEN.
/// </summary>
public static class SonosConstants
{
    /// <summary>
    /// Header and well-known LAN API key used by official and third-party LAN clients.
    /// </summary>
    public const string LocalApiToken = "123e4567-e89b-12d3-a456-426655440000";

    /// <summary>
    /// WebSocket subprotocol for the local Control API.
    /// </summary>
    public const string WebsocketProtocol = "v1.api.smartspeaker.audio";

    /// <summary>
    /// App id sent in playbackSession create/join.
    /// </summary>
    public const string AppId = "Jellyfin.Plugin.Sonos";
}
