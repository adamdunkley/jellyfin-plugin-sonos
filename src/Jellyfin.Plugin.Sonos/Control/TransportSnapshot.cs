using System;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.Sonos.Api.Models;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Snapshot of transport state on a coordinator.
/// </summary>
public sealed class TransportSnapshot
{
    /// <summary>Gets the playback state.</summary>
    public PlaybackState State { get; init; }

    /// <summary>Gets position in ticks.</summary>
    public long PositionTicks { get; init; }

    /// <summary>Gets volume 0-100.</summary>
    public int Volume { get; init; }

    /// <summary>Gets a value indicating whether output is muted.</summary>
    public bool Muted { get; init; }

    /// <summary>Gets the Cloud Queue item id the speaker is playing, when reported.</summary>
    public string? CurrentItemId { get; init; }

    /// <summary>Gets the current track URI (SOAP TrackURI / stream URL), when reported.</summary>
    public string? CurrentUri { get; init; }

    /// <summary>
    /// Reads Cloud Queue <c>itemId</c> from a LAN playbackStatus body.
    /// </summary>
    /// <param name="data">Command body.</param>
    /// <returns>The current item id, or null when the speaker did not report one.</returns>
    public static string? ItemIdFromStatus(JsonNode? data)
    {
        if (data is not JsonObject obj)
        {
            return null;
        }

        return ReadString(obj["itemId"])
            ?? ReadString((obj["currentItem"] as JsonObject)?["itemId"])
            ?? ReadString((obj["currentItem"] as JsonObject)?["id"]);
    }

    /// <summary>
    /// Reads <c>positionMillis</c> from a LAN playbackStatus body (number or string).
    /// </summary>
    /// <param name="data">Command body.</param>
    /// <returns>Position in ticks.</returns>
    public static long PositionTicksFromStatus(JsonNode? data)
    {
        if (data is not JsonObject obj)
        {
            return 0;
        }

        var node = obj["positionMillis"] ?? (obj["position"] as JsonObject)?["positionMillis"];
        if (node is not JsonValue value)
        {
            return 0;
        }

        long millis;
        if (value.TryGetValue<long>(out millis))
        {
            // handled
        }
        else if (value.TryGetValue<int>(out var asInt))
        {
            millis = asInt;
        }
        else if (value.TryGetValue<string>(out var asString)
                 && long.TryParse(asString, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            millis = parsed;
        }
        else
        {
            return 0;
        }

        if (millis <= 0)
        {
            return 0;
        }

        return millis * TimeSpan.TicksPerMillisecond;
    }

    /// <summary>
    /// Reads group volume and mute from a LAN groupVolume body.
    /// </summary>
    /// <param name="data">Command body.</param>
    /// <returns>Volume 0-100 and mute.</returns>
    public static (int Volume, bool Muted) VolumeFromStatus(JsonNode? data)
    {
        if (data is not JsonObject obj)
        {
            return (0, false);
        }

        var volumeNode = obj["volume"];
        if (volumeNode is JsonObject nested)
        {
            volumeNode = nested["volume"] ?? nested["value"];
        }

        return (Math.Clamp(ReadInt(volumeNode), 0, 100), ReadBool(obj["muted"]));
    }

    private static int ReadInt(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<int>(out var asInt))
        {
            return asInt;
        }

        if (value.TryGetValue<long>(out var asLong))
        {
            return (int)Math.Clamp(asLong, int.MinValue, int.MaxValue);
        }

        if (value.TryGetValue<double>(out var asDouble))
        {
            return (int)Math.Clamp(asDouble, 0, 100);
        }

        if (value.TryGetValue<string>(out var asString)
            && int.TryParse(asString, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static bool ReadBool(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<bool>(out var asBool))
        {
            return asBool;
        }

        if (value.TryGetValue<string>(out var asString)
            && bool.TryParse(asString, out var parsed))
        {
            return parsed;
        }

        return false;
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var asString) && !string.IsNullOrWhiteSpace(asString))
        {
            return asString;
        }

        return null;
    }
}
