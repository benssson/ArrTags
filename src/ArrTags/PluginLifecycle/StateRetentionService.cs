using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Reconciliation;
using ArrTags.State;
using Microsoft.Extensions.Hosting;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The hosted, bounded, cancellation-aware retention maintenance. It periodically
/// applies the versioned state retention policy (bounded cache TTL/quota and
/// terminal-provenance cleanup), the metadata freshness retention policy, and the
/// authoritative artifact retention/garbage collection, so terminal provenance and
/// superseded render output are reclaimed in production rather than only in tests.
/// </summary>
/// <remarks>
/// The first pass runs immediately on a tracked background task and never blocks
/// host startup; later passes run on a fixed internal scheduling interval. The
/// pass is bounded and performs no provider, rendering, or image work. On
/// shutdown the loop is cancelled and awaited within a bounded timeout, and the
/// durable records remain authoritative for the next pass. Metadata
/// last-known-good state is explicitly exempt from the render work-cache policy:
/// its usability is bounded by freshness, not by cache eviction.
/// </remarks>
public sealed class StateRetentionService : IHostedService, IDisposable
{
    /// <summary>
    /// The default fixed interval between retention passes.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// The bounded time a graceful shutdown is allowed to spend awaiting the loop.
    /// </summary>
    public static readonly TimeSpan BoundedShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly StateRepository _repository;
    private readonly MetadataStateStore _metadata;
    private readonly ArtifactRetention _artifacts;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _shutdownTimeout;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateRetentionService"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository.</param>
    /// <param name="metadata">The metadata state store whose expired records are pruned.</param>
    /// <param name="artifacts">The authoritative artifact retention policy.</param>
    /// <param name="interval">An optional bounded pass interval; defaults to <see cref="DefaultInterval"/>.</param>
    /// <param name="boundedShutdownTimeout">An optional bounded shutdown timeout; defaults to <see cref="BoundedShutdownTimeout"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public StateRetentionService(
        StateRepository repository,
        MetadataStateStore metadata,
        ArtifactRetention artifacts,
        TimeSpan? interval = null,
        TimeSpan? boundedShutdownTimeout = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _interval = interval is { } value && value > TimeSpan.Zero ? value : DefaultInterval;
        _shutdownTimeout = boundedShutdownTimeout ?? BoundedShutdownTimeout;
    }

    /// <summary>
    /// Gets the bounded interval between retention passes.
    /// </summary>
    public TimeSpan Interval => _interval;

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
        _loop = RunLoopAsync(cts.Token);
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
        if (loop is not null)
        {
            try
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bounded.CancelAfter(_shutdownTimeout);
                await loop.WaitAsync(bounded.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A cancelled or timed-out pass is bounded and safe: the durable
                // records remain authoritative for the next retention pass.
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
        _loop = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Applies one bounded retention pass and returns the number of removed
    /// records and artifacts. The pass never throws for an ordinary storage
    /// failure; a failed component leaves the durable state authoritative for
    /// the next pass.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The number of removed records and artifacts.</returns>
    public int RunRetentionPass(DateTimeOffset now)
    {
        var removed = _repository.ApplyRetention(now, new[] { MetadataStateStore.RecordKind });
        removed += _metadata.ApplyRetention(now);
        removed += _artifacts.Apply(now);
        return removed;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RunRetentionPass(DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Retention is maintenance: a bounded failure must never take
                // down the host or the work pipeline.
            }

            try
            {
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
