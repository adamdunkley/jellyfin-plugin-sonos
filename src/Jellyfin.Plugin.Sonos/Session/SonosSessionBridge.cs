using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Sonos.Api.Models;
using Jellyfin.Plugin.Sonos.Discovery;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Session;

/// <summary>
/// Keeps one controllable Jellyfin session per available Sonos coordinator.
/// </summary>
public sealed class SonosSessionBridge : BackgroundService
{
    private const string ClientName = "Sonos";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly ISessionManager _sessions;
    private readonly IUserManager _users;
    private readonly ILibraryManager _library;
    private readonly IImageProcessor _images;
    private readonly PlayerRegistry _registry;
    private readonly SonosPlaybackService _playback;
    private readonly ILogger<SonosSessionBridge> _logger;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosSessionBridge"/> class.
    /// </summary>
    /// <param name="sessions">Session manager.</param>
    /// <param name="users">User manager.</param>
    /// <param name="library">Library used to resolve now-playing artwork.</param>
    /// <param name="images">Image cache tags for the now-playing bar.</param>
    /// <param name="registry">Player registry.</param>
    /// <param name="playback">Playback service.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public SonosSessionBridge(
        ISessionManager sessions,
        IUserManager users,
        ILibraryManager library,
        IImageProcessor images,
        PlayerRegistry registry,
        SonosPlaybackService playback,
        ILogger<SonosSessionBridge> logger,
        ILoggerFactory loggerFactory)
    {
        _sessions = sessions;
        _users = users;
        _library = library;
        _images = images;
        _registry = registry;
        _playback = playback;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sonos session bridge started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Plugin.Instance?.Configuration.Enabled == false)
                {
                    await _playback.StopOwnedPlaybackAsync(stoppingToken).ConfigureAwait(false);
                    await CloseOwnedSessionsAsync().ConfigureAwait(false);
                }
                else
                {
                    await SyncAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sonos session sync failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await CloseOwnedSessionsAsync().ConfigureAwait(false);
        _logger.LogInformation("Sonos session bridge stopped");
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        var snapshot = _registry.GetSnapshot();
        var coordinators = snapshot.Players
            .Where(p => p.Available && p.IsCoordinator)
            .ToArray();
        var wanted = coordinators.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var session in OwnedSessions().ToArray())
        {
            if (!wanted.Contains(session.DeviceId))
            {
                await CloseSessionAsync(session).ConfigureAwait(false);
            }
        }

        foreach (var player in coordinators)
        {
            var group = snapshot.Groups.FirstOrDefault(g =>
                string.Equals(g.CoordinatorId, player.Id, StringComparison.OrdinalIgnoreCase));
            var displayName = group is { MemberIds.Count: > 1 } ? group.Name : player.Name;
            await EnsureSessionAsync(player, displayName, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureSessionAsync(PlayerInfo player, string displayName, CancellationToken cancellationToken)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        var session = await _sessions.LogSessionActivity(
                ClientName,
                version,
                player.Id,
                displayName,
                player.Ip,
                ResolveUser())
            .ConfigureAwait(false);

        session.DeviceType = "cast";
        if (!string.Equals(session.DeviceName, displayName, StringComparison.Ordinal))
        {
            _sessions.UpdateDeviceName(session.Id, displayName);
        }

        var controller = session.SessionControllers.OfType<SonosSessionController>().FirstOrDefault();
        if (controller is null)
        {
            var reporter = new SessionPlaybackReporter(
                _sessions,
                _library,
                _images,
                _loggerFactory.CreateLogger<SessionPlaybackReporter>());
            controller = new SonosSessionController(
                session,
                player.Id,
                _playback,
                reporter,
                _loggerFactory.CreateLogger<SonosSessionController>());
            session.AddController(controller);
            _sessions.OnSessionControllerConnected(session);
        }

        _sessions.ReportCapabilities(session.Id, BuildCapabilities());

        var queueResult = await _playback.GetQueueAsync(player.Id, cancellationToken).ConfigureAwait(false);
        var queue = ActionResultReader.Value<QueueResponse>(queueResult);
        if (queue is not null)
        {
            var reporterField = session.SessionControllers.OfType<SonosSessionController>().First();
            await reporterField.ReportProgressIfNeededAsync(queue).ConfigureAwait(false);
        }
    }

    private IEnumerable<SessionInfo> OwnedSessions()
    {
        return _sessions.Sessions.Where(s =>
            string.Equals(s.Client, ClientName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task CloseOwnedSessionsAsync()
    {
        foreach (var session in OwnedSessions().ToArray())
        {
            await CloseSessionAsync(session).ConfigureAwait(false);
        }
    }

    private async Task CloseSessionAsync(SessionInfo session)
    {
        foreach (var controller in session.SessionControllers.OfType<SonosSessionController>())
        {
            controller.Deactivate();
        }

        try
        {
            await _sessions.CloseIfNeededAsync(session).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not close Sonos session {DeviceId}", session.DeviceId);
        }
    }

    private Jellyfin.Database.Implementations.Entities.User? ResolveUser()
    {
        if (!Guid.TryParse(Plugin.Instance?.Configuration.DefaultUserId, out var id) || id == Guid.Empty)
        {
            return null;
        }

        return _users.GetUserById(id);
    }

    private static ClientCapabilities BuildCapabilities()
    {
        return new ClientCapabilities
        {
            PlayableMediaTypes = [MediaType.Audio],
            SupportsMediaControl = true,
            SupportsPersistentIdentifier = true,
            DeviceProfile = null,
            SupportedCommands =
            [
                GeneralCommandType.VolumeUp,
                GeneralCommandType.VolumeDown,
                GeneralCommandType.Mute,
                GeneralCommandType.Unmute,
                GeneralCommandType.ToggleMute,
                GeneralCommandType.SetVolume,
                GeneralCommandType.SetRepeatMode,
                GeneralCommandType.SetShuffleQueue
            ]
        };
    }
}
