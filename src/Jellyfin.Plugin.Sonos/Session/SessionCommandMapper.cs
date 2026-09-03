using System;
using System.Globalization;
using Jellyfin.Plugin.Sonos.Api.Models;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Sonos.Session;

/// <summary>
/// Maps Jellyfin session Play / Playstate / GeneralCommand messages onto <c>/Sonos</c> request bodies.
/// </summary>
public static class SessionCommandMapper
{
    /// <summary>
    /// Maps a session play command to Queue/Play or Queue/Add.
    /// </summary>
    /// <param name="request">Session play body.</param>
    /// <param name="targetId">Coordinator id.</param>
    /// <returns>The mapped command.</returns>
    public static MappedPlayCommand MapPlay(PlayRequest request, string targetId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = request.ItemIds ?? [];
        var startIndex = request.StartIndex ?? 0;
        var ticks = request.StartPositionTicks ?? 0;

        if (request.PlayCommand == PlayCommand.PlayNext)
        {
            return MappedPlayCommand.Enqueue(new AddQueueRequest
            {
                TargetId = targetId,
                ItemIds = ids,
                Mode = "Next"
            });
        }

        if (request.PlayCommand == PlayCommand.PlayLast)
        {
            return MappedPlayCommand.Enqueue(new AddQueueRequest
            {
                TargetId = targetId,
                ItemIds = ids,
                Mode = "Last"
            });
        }

        return MappedPlayCommand.PlayNow(new PlayQueueRequest
        {
            TargetId = targetId,
            ItemIds = ids,
            StartIndex = startIndex,
            StartPositionTicks = ticks
        });
    }

    /// <summary>
    /// Maps a session playstate command.
    /// </summary>
    /// <param name="request">Session playstate body.</param>
    /// <param name="targetId">Coordinator id.</param>
    /// <param name="current">Last known playback state.</param>
    /// <returns>The plugin playstate body, or null when unsupported.</returns>
    public static Api.Models.PlaystateRequest? MapPlaystate(
        MediaBrowser.Model.Session.PlaystateRequest request,
        string targetId,
        PlaybackState current)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = request.Command switch
        {
            PlaystateCommand.Stop => "Stop",
            PlaystateCommand.Pause => "Pause",
            PlaystateCommand.Unpause => "Play",
            PlaystateCommand.NextTrack => "Next",
            PlaystateCommand.PreviousTrack => "Previous",
            PlaystateCommand.Seek => "Seek",
            PlaystateCommand.PlayPause => current == PlaybackState.Playing ? "Pause" : "Play",
            _ => null
        };

        if (command is null)
        {
            return null;
        }

        return new Api.Models.PlaystateRequest
        {
            TargetId = targetId,
            Command = command,
            PositionTicks = request.SeekPositionTicks
        };
    }

    /// <summary>
    /// Maps a session general command (volume, mute, repeat, shuffle).
    /// </summary>
    /// <param name="command">General command.</param>
    /// <param name="targetId">Coordinator id.</param>
    /// <param name="queue">Last queue snapshot, used for toggle/relative volume.</param>
    /// <returns>The plugin playstate body, or null when unsupported.</returns>
    public static Api.Models.PlaystateRequest? MapGeneralCommand(GeneralCommand command, string targetId, QueueResponse? queue)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Name switch
        {
            GeneralCommandType.Mute => new Api.Models.PlaystateRequest { TargetId = targetId, Command = "Mute" },
            GeneralCommandType.Unmute => new Api.Models.PlaystateRequest { TargetId = targetId, Command = "Unmute" },
            GeneralCommandType.ToggleMute => new Api.Models.PlaystateRequest
            {
                TargetId = targetId,
                Command = queue?.Muted == true ? "Unmute" : "Mute"
            },
            GeneralCommandType.SetVolume => new Api.Models.PlaystateRequest
            {
                TargetId = targetId,
                Command = "SetVolume",
                Volume = ReadIntArgument(command, "Volume") ?? queue?.Volume ?? 0
            },
            GeneralCommandType.VolumeUp => new Api.Models.PlaystateRequest
            {
                TargetId = targetId,
                Command = "SetVolume",
                Volume = Math.Clamp((queue?.Volume ?? 0) + 5, 0, 100)
            },
            GeneralCommandType.VolumeDown => new Api.Models.PlaystateRequest
            {
                TargetId = targetId,
                Command = "SetVolume",
                Volume = Math.Clamp((queue?.Volume ?? 0) - 5, 0, 100)
            },
            GeneralCommandType.SetRepeatMode => new Api.Models.PlaystateRequest
            {
                TargetId = targetId,
                Command = "SetRepeat",
                Repeat = MapRepeat(ReadArgument(command, "RepeatMode"))
            },
            GeneralCommandType.SetShuffleQueue => new Api.Models.PlaystateRequest
            {
                TargetId = targetId,
                Command = "SetShuffle",
                Shuffle = string.Equals(ReadArgument(command, "ShuffleMode"), "Shuffle", StringComparison.OrdinalIgnoreCase)
            },
            _ => null
        };
    }

    /// <summary>
    /// Maps plugin repeat (None/All/One) to Jellyfin <see cref="RepeatMode"/>.
    /// </summary>
    /// <param name="repeat">Plugin repeat.</param>
    /// <returns>Session repeat mode.</returns>
    public static RepeatMode ToRepeatMode(string? repeat)
    {
        return repeat switch
        {
            "All" => RepeatMode.RepeatAll,
            "One" => RepeatMode.RepeatOne,
            _ => RepeatMode.RepeatNone
        };
    }

    private static string MapRepeat(string? value)
    {
        if (string.Equals(value, "RepeatAll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "All", StringComparison.OrdinalIgnoreCase))
        {
            return "All";
        }

        if (string.Equals(value, "RepeatOne", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "One", StringComparison.OrdinalIgnoreCase))
        {
            return "One";
        }

        return "None";
    }

    private static string? ReadArgument(GeneralCommand command, string key)
    {
        if (command.Arguments is not null && command.Arguments.TryGetValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    private static int? ReadIntArgument(GeneralCommand command, string key)
    {
        var raw = ReadArgument(command, key) ?? ReadArgument(command, ToCamel(key));
        if (raw is null)
        {
            return null;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDouble))
        {
            return (int)Math.Round(asDouble, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static string ToCamel(string key)
    {
        if (string.IsNullOrEmpty(key) || char.IsLower(key[0]))
        {
            return key;
        }

        return char.ToLowerInvariant(key[0]) + key[1..];
    }
}
