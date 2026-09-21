using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using Microsoft.Extensions.Hosting;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The hosted startup recovery for durable artwork operations. After the
/// lifecycle service has cleared a stale fence, this service runs one bounded,
/// cancellation-aware recovery scan over the persisted operation records,
/// limited by the current <see cref="OperationalLimits.ReconciliationBatchSize"/>.
/// The scan never blocks host startup: it runs on a tracked background task, and
/// the per-subject <see cref="IArtworkRecoveryGate"/> independently guarantees
/// that no subject with a non-terminal operation accepts new work.
/// </summary>
/// <remarks>
/// A batch beyond the configured bound is recovered lazily by the work pipeline
/// before that subject's next work item, so no untracked non-terminal operation
/// can be driven by new work. On shutdown the scan is cancelled and awaited
/// within a bounded timeout; its durable operation records remain authoritative
/// for the next reconciliation.
/// </remarks>
public sealed class ArtworkStartupRecoveryService : IHostedService, IDisposable
{
    /// <summary>
    /// The bounded time a graceful shutdown is allowed to spend awaiting the
    /// startup scan before the host stops waiting.
    /// </summary>
    public static readonly TimeSpan BoundedShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly IArtworkRecoveryGate _gate;
    private readonly ConfigurationSnapshotService _configuration;
    private readonly TimeSpan _shutdownTimeout;
    private CancellationTokenSource? _cts;
    private Task? _scan;
    private int _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkStartupRecoveryService"/> class.
    /// </summary>
    /// <param name="gate">The bounded per-subject and startup artwork recovery gate.</param>
    /// <param name="configuration">The current public configuration snapshot used for the scan bound.</param>
    /// <param name="boundedShutdownTimeout">An optional bounded shutdown timeout; defaults to <see cref="BoundedShutdownTimeout"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkStartupRecoveryService(
        IArtworkRecoveryGate gate,
        ConfigurationSnapshotService configuration,
        TimeSpan? boundedShutdownTimeout = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _shutdownTimeout = boundedShutdownTimeout ?? BoundedShutdownTimeout;
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
        _scan = RunScanAsync(cts.Token);
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

        var scan = Interlocked.Exchange(ref _scan, null);
        if (scan is not null)
        {
            try
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bounded.CancelAfter(_shutdownTimeout);
                await scan.WaitAsync(bounded.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A cancelled or timed-out scan is bounded and safe: the durable
                // records remain authoritative for the next reconciliation.
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The host must still be allowed to shut down.
            }
        }

        cts?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _scan = null;

        GC.SuppressFinalize(this);
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var batchSize = _configuration.Current.Limits.ReconciliationBatchSize;
            await _gate.RecoverStartupAsync(batchSize, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The scan was cancelled during host shutdown.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A bounded startup scan must never prevent the plugin from loading;
            // the per-subject gate still recovers each subject before work.
        }
    }
}
