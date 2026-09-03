namespace Jellyfin.Plugin.Sonos.Discovery;

/// <summary>
/// Parsed UPnP device_description.xml fields.
/// </summary>
public sealed class DeviceDescription
{
    /// <summary>Gets the RINCON UUID without uuid: prefix.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the room name.</summary>
    public string RoomName { get; init; } = string.Empty;

    /// <summary>Gets the model number (S6, S39, …).</summary>
    public string ModelNumber { get; init; } = string.Empty;

    /// <summary>Gets the model name.</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
