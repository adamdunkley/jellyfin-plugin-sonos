using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Api.Models;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Session;

/// <summary>
/// Pushes Sonos queue snapshots into a Jellyfin session so the now-playing bar follows the speaker.
/// </summary>
public sealed class SessionPlaybackReporter
{
    private readonly ISessionManager _sessions;
    private readonly ILibraryManager _library;
    private readonly IImageProcessor _images;
    private readonly ILogger _logger;
    private Guid _lastItemId;
    private string _lastQueueItemId = string.Empty;
    private PlaybackState _lastState = PlaybackState.Stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionPlaybackReporter"/> class.
    /// </summary>
    /// <param name="sessions">Session manager.</param>
    /// <param name="library">Library used to resolve artwork.</param>
    /// <param name="images">Image cache tags for the now-playing bar.</param>
    /// <param name="logger">Logger.</param>
    public SessionPlaybackReporter(
        ISessionManager sessions,
        ILibraryManager library,
        IImageProcessor images,
        ILogger logger)
    {
        _sessions = sessions;
        _library = library;
        _images = images;
        _logger = logger;
    }

    /// <summary>
    /// Reports start, progress, or stop from a queue snapshot.
    /// </summary>
    /// <param name="sessionId">Jellyfin session id.</param>
    /// <param name="queue">Queue snapshot.</param>
    /// <param name="forceStart">True to emit PlaybackStart even when the item is unchanged.</param>
    /// <returns>A task.</returns>
    public async Task ReportAsync(string sessionId, QueueResponse queue, bool forceStart)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(queue);

        if (queue.Items.Count == 0 || queue.State == PlaybackState.Stopped)
        {
            if (_lastState != PlaybackState.Stopped && _lastItemId != Guid.Empty)
            {
                try
                {
                    await _sessions.OnPlaybackStopped(new PlaybackStopInfo
                    {
                        ItemId = _lastItemId,
                        SessionId = sessionId,
                        PositionTicks = queue.PositionTicks,
                        Failed = false
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to report Sonos playback stop");
                }
            }

            _lastState = PlaybackState.Stopped;
            _lastItemId = Guid.Empty;
            _lastQueueItemId = string.Empty;
            return;
        }

        var index = Math.Clamp(queue.CurrentIndex, 0, queue.Items.Count - 1);
        var current = queue.Items[index];
        var info = BuildProgress(sessionId, queue, current, index);

        try
        {
            if (forceStart || ShouldStart(current, _lastItemId, _lastQueueItemId, _lastState))
            {
                await _sessions.OnPlaybackStart(new PlaybackStartInfo
                {
                    CanSeek = info.CanSeek,
                    Item = info.Item,
                    ItemId = info.ItemId,
                    SessionId = info.SessionId,
                    IsPaused = info.IsPaused,
                    IsMuted = info.IsMuted,
                    PositionTicks = info.PositionTicks,
                    VolumeLevel = info.VolumeLevel,
                    PlayMethod = info.PlayMethod,
                    RepeatMode = info.RepeatMode,
                    PlaybackOrder = info.PlaybackOrder,
                    NowPlayingQueue = info.NowPlayingQueue,
                    PlaylistItemId = info.PlaylistItemId
                }).ConfigureAwait(false);
            }
            else
            {
                await _sessions.OnPlaybackProgress(info, false).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to report Sonos playback to session {SessionId}", sessionId);
        }

        _lastItemId = current.ItemId;
        _lastQueueItemId = current.QueueItemId;
        _lastState = queue.State;
    }

    private PlaybackProgressInfo BuildProgress(string sessionId, QueueResponse queue, QueueItemDto current, int index)
    {
        var items = queue.Items.Select((item, i) => new QueueItem
        {
            Id = item.ItemId,
            PlaylistItemId = PlaylistId(i)
        }).ToArray();

        return new PlaybackProgressInfo
        {
            CanSeek = true,
            Item = SessionNowPlayingMapper.FromQueueItem(current, ResolveLibraryItem(current.ItemId), _images),
            ItemId = current.ItemId,
            SessionId = sessionId,
            IsPaused = queue.State == PlaybackState.Paused,
            IsMuted = queue.Muted,
            PositionTicks = queue.PositionTicks,
            VolumeLevel = queue.Volume,
            PlayMethod = PlayMethod.DirectStream,
            RepeatMode = SessionCommandMapper.ToRepeatMode(queue.Repeat),
            PlaybackOrder = queue.Shuffle ? PlaybackOrder.Shuffle : PlaybackOrder.Default,
            NowPlayingQueue = items,
            PlaylistItemId = PlaylistId(index)
        };
    }

    private BaseItem? ResolveLibraryItem(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            return null;
        }

        try
        {
            return _library.GetItemById(itemId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve library item {ItemId} for Sonos now playing", itemId);
            return null;
        }
    }

    internal static bool ShouldStart(
        QueueItemDto current,
        Guid lastItemId,
        string lastQueueItemId,
        PlaybackState lastState)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (lastState == PlaybackState.Stopped)
        {
            return true;
        }

        if (current.ItemId != lastItemId)
        {
            return true;
        }

        return !string.Equals(current.QueueItemId, lastQueueItemId, StringComparison.OrdinalIgnoreCase);
    }

    private static string PlaylistId(int index) => "playlistItem" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
