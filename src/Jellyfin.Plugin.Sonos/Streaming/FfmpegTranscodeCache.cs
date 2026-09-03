using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Sonos.Streaming;

/// <summary>
/// Transcodes audio to a finite cache file so Range and Content-Length work.
/// </summary>
public sealed class FfmpegTranscodeCache
{
    private readonly IMediaEncoder _encoder;
    private readonly IApplicationPaths _paths;
    private readonly ILogger<FfmpegTranscodeCache> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="FfmpegTranscodeCache"/> class.
    /// </summary>
    /// <param name="encoder">Jellyfin media encoder.</param>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger.</param>
    public FfmpegTranscodeCache(IMediaEncoder encoder, IApplicationPaths paths, ILogger<FfmpegTranscodeCache> logger)
    {
        _encoder = encoder;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Returns a completed transcode file for the decision.
    /// </summary>
    /// <param name="sourcePath">Library file path.</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="sourceMtime">Source last write UTC ticks.</param>
    /// <param name="decision">Plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Output path.</returns>
    public async Task<string> GetOrCreateAsync(
        string sourcePath,
        Guid itemId,
        long sourceMtime,
        TranscodeDecision decision,
        CancellationToken cancellationToken)
    {
        var ext = decision.Container == "aac" ? ".m4a" : ".flac";
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{itemId:N}_{decision.Container}_{decision.SampleRate}_{decision.BitDepth}_{sourceMtime}{ext}");
        var dir = Path.Combine(_paths.CachePath, "sonos-transcodes");
        Directory.CreateDirectory(dir);
        var output = Path.Combine(dir, key);
        if (File.Exists(output) && new FileInfo(output).Length > 0)
        {
            return output;
        }

        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(output) && new FileInfo(output).Length > 0)
            {
                return output;
            }

            await RunFfmpegAsync(sourcePath, output, decision, cancellationToken).ConfigureAwait(false);
            TrimCache(dir);
            return output;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RunFfmpegAsync(string source, string output, TranscodeDecision decision, CancellationToken cancellationToken)
    {
        var args = new List<string> { "-y", "-i", source, "-vn", "-map", "0:a:0" };
        if (decision.Container == "aac")
        {
            args.AddRange(["-c:a", "aac", "-b:a", "256k"]);
        }
        else
        {
            args.AddRange(["-c:a", "flac", "-sample_fmt", "s16"]);
        }

        args.AddRange(["-ar", decision.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2", output]);

        var psi = new ProcessStartInfo
        {
            FileName = _encoder.EncoderPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        _logger.LogInformation("Transcoding to {Container} {Rate} Hz ({Reason})", decision.Container, decision.SampleRate, decision.Reason);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg failed to start");
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (Exception)
            {
                // ignore
            }

            throw;
        }

        await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0 || !File.Exists(output))
        {
            throw new InvalidOperationException("ffmpeg transcode failed with code " + process.ExitCode);
        }
    }

    private static void TrimCache(string dir)
    {
        var files = new DirectoryInfo(dir).GetFiles();
        if (files.Length <= 32)
        {
            return;
        }

        foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc).Take(files.Length - 32))
        {
            try
            {
                file.Delete();
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}
