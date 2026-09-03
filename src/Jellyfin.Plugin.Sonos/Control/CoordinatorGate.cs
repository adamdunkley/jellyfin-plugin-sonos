using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Sonos.Control;

/// <summary>
/// Serializes control commands per coordinator.
/// </summary>
public sealed class CoordinatorGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="action"/> with exclusive access for <paramref name="coordinatorId"/>.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="coordinatorId">Coordinator id.</param>
    /// <param name="action">Work.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action result.</returns>
    public async Task<T> RunAsync<T>(string coordinatorId, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(coordinatorId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> with exclusive access for <paramref name="coordinatorId"/>.
    /// </summary>
    /// <param name="coordinatorId">Coordinator id.</param>
    /// <param name="action">Work.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public Task RunAsync(string coordinatorId, Func<Task> action, CancellationToken cancellationToken)
    {
        return RunAsync(
            coordinatorId,
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }
}
