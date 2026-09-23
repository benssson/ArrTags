using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Reconciliation;
using Microsoft.Extensions.Hosting;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The hosted post-save reconciliation trigger (ADR-016 clause 5 second bullet).
/// A successful configuration replacement calls <see cref="RequestReconciliation"/>,
/// which enqueues one bounded request and returns immediately; a hosted loop then
/// runs the existing bounded <see cref="LibraryReconciliationService"/> off the
/// save thread and enqueues the same provider-neutral work hints as the
/// scheduled, manual, and post-scan triggers. It is never a synchronous
/// full-library scan, never blocks the save response, and never throws into the
/// host.
/// </summary>
/// <remarks>
/// The request slot is bounded: at most one reconciliation is pending at a time,
/// and a request that arrives while a reconciliation is running is coalesced to a
/// single bounded rerun. The rerun is required because a run reads the current
/// snapshot once when it starts, so a replacement that lands during a run would
/// otherwise be missed until the next library event, webhook, post-scan, or
/// scheduled run. On shutdown the loop is cancelled and awaited within a bounded
/// timeout; the durable state remains authoritative for the next reconciliation.
/// </remarks>
public sealed class ConfigurationReconciliationTrigger : IHostedService, IConfigurationReconciliationTrigger, IDisposable
{
    /// <summary>
    /// The bounded time a graceful shutdown is allowed to spend awaiting the
    /// loop before the host stops waiting.
    /// </summary>
    public static readonly TimeSpan BoundedShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly LibraryReconciliationService _reconciliation;
    private readonly TimeSpan _shutdownTimeout;
    private readonly SemaphoreSlim _signal = new SemaphoreSlim(0, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _requestedVersion;
    private int _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationReconciliationTrigger"/> class.
    /// </summary>
    /// <param name="reconciliation">The existing bounded full-reconciliation service.</param>
    /// <param name="boundedShutdownTimeout">An optional bounded shutdown timeout; defaults to <see cref="BoundedShutdownTimeout"/>.</param>
    /// <exception cref="ArgumentNullException">The reconciliation service is <see langword="null"/>.</exception>
    public ConfigurationReconciliationTrigger(
        LibraryReconciliationService reconciliation,
        TimeSpan? boundedShutdownTimeout = null)
    {
        _reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
        _shutdownTimeout = boundedShutdownTimeout ?? BoundedShutdownTimeout;
    }

    /// <inheritdoc />
    public void RequestReconciliation()
    {
        if (Volatile.Read(ref _started) == 0)
        {
            // The hosted loop has not started or has stopped; the next library
            // event, webhook, post-scan, or scheduled run remains authoritative.
            return;
        }

        try
        {
            Interlocked.Increment(ref _requestedVersion);
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Coalesced: one reconciliation is already pending and will observe
            // this request through the version comparison in the loop.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A post-save trigger must never surface a failure to the save path.
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        _loop = RunAsync(cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);

        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        var loop = Interlocked.Exchange(ref _loop, null);

        try
        {
            if (loop is not null)
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bounded.CancelAfter(_shutdownTimeout);
                await loop.WaitAsync(bounded.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled or timed-out loop wait is bounded and safe; the durable
            // state remains authoritative for the next reconciliation.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The host must still be allowed to shut down.
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _signal.Dispose();

        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        long servedVersion = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                continue;
            }

            long targetVersion;
            do
            {
                // Read the target before the run: a request that arrives during
                // the run bumps the version and is observed after it completes, so
                // exactly one bounded rerun serves the latest replacement.
                targetVersion = Volatile.Read(ref _requestedVersion);
                if (targetVersion == servedVersion)
                {
                    // A coalesced or leftover wakeup with no new request: nothing
                    // to reconcile, so the loop waits for the next request rather
                    // than running an extra full reconciliation.
                    break;
                }

                try
                {
                    await _reconciliation
                        .ReconcileAsync(LibraryReconciliationSource.PostSave, progress: null, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // A bounded post-save reconciliation is best-effort; the
                    // scheduled reconciliation remains authoritative for any
                    // missed work.
                }

                servedVersion = targetVersion;
            }
            while (Volatile.Read(ref _requestedVersion) != servedVersion
                && !cancellationToken.IsCancellationRequested);
        }
    }
}
