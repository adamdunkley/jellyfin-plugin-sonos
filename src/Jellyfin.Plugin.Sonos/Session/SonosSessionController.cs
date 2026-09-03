using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Sonos.Api.Models;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Session;

/// <summary>
/// Session controller that forwards Play To commands to <see cref="SonosPlaybackService"/>.
/// </summary>
public sealed class SonosSessionController : ISessionController
{
    private readonly SessionInfo _session;
    private readonly string _targetId;
    private readonly SonosPlaybackService _playback;
    private readonly SessionPlaybackReporter _reporter;
    private readonly ILogger _logger;
    private QueueResponse? _lastQueue;
    private bool _active = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonosSessionController"/> class.
    /// </summary>
    /// <param name="session">Owning session.</param>
    /// <param name="targetId">Coordinator RINCON.</param>
    /// <param name="playback">Playback service.</param>
    /// <param name="reporter">Now-playing reporter.</param>
    /// <param name="logger">Logger.</param>
    public SonosSessionController(
        SessionInfo session,
        string targetId,
        SonosPlaybackService playback,
        SessionPlaybackReporter reporter,
        ILogger logger)
    {
        _session = session;
        _targetId = targetId;
        _playback = playback;
        _reporter = reporter;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSessionActive => _active;

    /// <inheritdoc />
    public bool SupportsMediaControl => true;

    /// <summary>Coordinator this session represents.</summary>
    public string TargetId => _targetId;

    /// <summary>Marks the session inactive so Jellyfin can close it.</summary>
    public void Deactivate() => _active = false;

    /// <summary>
    /// Pushes the latest queue snapshot into the Jellyfin session (now-playing bar).
    /// </summary>
    /// <param name="queue">Queue snapshot.</param>
    /// <returns>A task.</returns>
    public Task ReportProgressIfNeededAsync(QueueResponse queue)
    {
        _lastQueue = queue;
        return _reporter.ReportAsync(_session.Id, queue, false);
    }

    /// <inheritdoc />
    public async Task SendMessage<T>(SessionMessageType name, Guid messageId, T data, CancellationToken cancellationToken)
    {
        try
        {
            switch (name)
            {
                case SessionMessageType.Play when data is PlayRequest play:
                    await HandlePlayAsync(play, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionMessageType.Playstate when data is MediaBrowser.Model.Session.PlaystateRequest playstate:
                    await HandlePlaystateAsync(playstate, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionMessageType.GeneralCommand when data is GeneralCommand general:
                    await HandleGeneralAsync(general, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sonos session {Target} failed to handle {Message}", _targetId, name);
            throw;
        }
    }

    private async Task HandlePlayAsync(PlayRequest request, CancellationToken cancellationToken)
    {
        var mapped = SessionCommandMapper.MapPlay(request, _targetId);
        var userId = request.ControllingUserId != Guid.Empty
            ? request.ControllingUserId
            : ResolveFallbackUser();

        ActionResult result;
        if (mapped.Play is not null)
        {
            result = await _playback.PlayAsync(mapped.Play, userId, cancellationToken).ConfigureAwait(false);
        }
        else if (mapped.Add is not null)
        {
            result = await _playback.AddAsync(mapped.Add, userId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return;
        }

        if (ActionResultReader.ShouldIgnorePlayFailure(result))
        {
            _logger.LogInformation(
                "Sonos session {Target} ignored non-audio play: {Message}",
                _targetId,
                ActionResultReader.Message(result));
            return;
        }

        EnsureSuccess(result);
        await ReportFromResultAsync(result, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandlePlaystateAsync(MediaBrowser.Model.Session.PlaystateRequest request, CancellationToken cancellationToken)
    {
        var mapped = SessionCommandMapper.MapPlaystate(request, _targetId, _lastQueue?.State ?? PlaybackState.Stopped);
        if (mapped is null)
        {
            return;
        }

        var result = await _playback.PlaystateAsync(mapped, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        await ReportFromResultAsync(result, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleGeneralAsync(GeneralCommand command, CancellationToken cancellationToken)
    {
        var mapped = SessionCommandMapper.MapGeneralCommand(command, _targetId, _lastQueue);
        if (mapped is null)
        {
            return;
        }

        var result = await _playback.PlaystateAsync(mapped, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        await ReportFromResultAsync(result, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReportFromResultAsync(ActionResult result, bool forceStart, CancellationToken cancellationToken)
    {
        var queue = ActionResultReader.Value<QueueResponse>(result);
        if (queue is null)
        {
            var fetched = await _playback.GetQueueAsync(_targetId, cancellationToken).ConfigureAwait(false);
            queue = ActionResultReader.Value<QueueResponse>(fetched);
        }

        if (queue is null)
        {
            return;
        }

        _lastQueue = queue;
        await _reporter.ReportAsync(_session.Id, queue, forceStart).ConfigureAwait(false);
    }

    private static void EnsureSuccess(ActionResult result)
    {
        if (!ActionResultReader.IsSuccess(result))
        {
            throw new InvalidOperationException(ActionResultReader.Message(result));
        }
    }

    private static Guid ResolveFallbackUser()
    {
        if (Guid.TryParse(Plugin.Instance?.Configuration.DefaultUserId, out var id) && id != Guid.Empty)
        {
            return id;
        }

        throw new InvalidOperationException("No Jellyfin user is available for library access. Set Default user on the Sonos plugin page.");
    }
}
