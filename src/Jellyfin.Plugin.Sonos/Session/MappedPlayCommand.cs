using Jellyfin.Plugin.Sonos.Api.Models;

namespace Jellyfin.Plugin.Sonos.Session;

/// <summary>
/// A session Play command mapped onto Queue/Play or Queue/Add.
/// </summary>
public sealed class MappedPlayCommand
{
    private MappedPlayCommand(PlayQueueRequest? play, AddQueueRequest? add)
    {
        Play = play;
        Add = add;
    }

    /// <summary>Gets a Queue/Play body when this is PlayNow.</summary>
    public PlayQueueRequest? Play { get; }

    /// <summary>Gets a Queue/Add body when this is PlayNext/PlayLast.</summary>
    public AddQueueRequest? Add { get; }

    /// <summary>Creates a PlayNow mapping.</summary>
    /// <param name="request">Play body.</param>
    /// <returns>The mapping.</returns>
    public static MappedPlayCommand PlayNow(PlayQueueRequest request) => new(request, null);

    /// <summary>Creates a Queue/Add mapping.</summary>
    /// <param name="request">Add body.</param>
    /// <returns>The mapping.</returns>
    public static MappedPlayCommand Enqueue(AddQueueRequest request) => new(null, request);
}
