using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using Microsoft.Extensions.Hosting;

namespace ArrTags.Updates;

/// <summary>
/// The hosted Phase 6 work consumer. It owns a small, bounded pool of
/// cancellation-aware workers that dequeue from the <see cref="LibraryWorkQueue"/>
/// and dispatch each item to the <see cref="IWorkItemProcessor"/> with the
/// bounded ADR-004 transient retry/backoff policy. Work is single-flight per
/// queue key, and the worker loop contains queue mechanics only; the
/// reconciliation pipeline is delegated to the processor boundary.
/// </summary>
/// <remarks>
/// The worker pool size is a fixed internal scheduling bound, not a persisted
/// user limit. On host shutdown the worker stops accepting new work, cancels the
/// queued and in-flight work, and awaits the workers within the bounded shutdown
/// timeout; no fire-and-forget task or unmanaged thread is created.
/// </remarks>
public sealed class LibraryWorkWorker : IHostedService, IDisposable
{
    /// <summary>
    /// The fixed number of concurrent work consumers.
    /// </summary>
    public const int DefaultWorkerCount = 4;

    /// <summary>
    /// The bounded time a graceful shutdown is allowed to spend awaiting
    /// workers before the host stops waiting.
    /// </summary>
    public static readonly TimeSpan BoundedShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly LibraryWorkQueue _queue;
    private readonly IWorkItemProcessor _processor;
    private readonly ConfigurationSnapshotService _configuration;
    private readonly int _workerCount;
    private readonly TimeSpan _shutdownTimeout;
    private CancellationTokenSource? _cts;
    private Task[] _workers = Array.Empty<Task>();
    private int _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWorkWorker"/> class.
    /// </summary>
    /// <param name="queue">The bounded coalescing work queue.</param>
    /// <param name="processor">The reconciliation pipeline dispatch boundary.</param>
    /// <param name="configuration">The current public configuration snapshot used for the retry policy.</param>
    /// <param name="workerCount">The optional bounded worker count; defaults to <see cref="DefaultWorkerCount"/>.</param>
    /// <param name="boundedShutdownTimeout">The optional bounded shutdown timeout; defaults to <see cref="BoundedShutdownTimeout"/>.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public LibraryWorkWorker(
        LibraryWorkQueue queue,
        IWorkItemProcessor processor,
        ConfigurationSnapshotService configuration,
        int workerCount = DefaultWorkerCount,
        TimeSpan? boundedShutdownTimeout = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _workerCount = workerCount < 1 ? 1 : workerCount;
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

        _queue.StartAccepting();

        var cts = new CancellationTokenSource();
        _cts = cts;

        var workers = new Task[_workerCount];
        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = RunWorkerAsync(cts.Token);
        }

        _workers = workers;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        _queue.StopAccepting();

        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        var workers = _workers;

        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(_shutdownTimeout);
            await Task.WhenAll(workers).WaitAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled or timed-out worker wait is bounded and safe: the
            // workers observe cancellation and exit, and the durable state
            // remains authoritative for the next reconciliation.
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
        _queue.StopAccepting();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        GC.SuppressFinalize(this);
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            LibraryWorkItem item;
            try
            {
                item = await _queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A queue failure must never take down the worker or the host.
                continue;
            }

            try
            {
                await ProcessWithRetryAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // An unexpected processor failure is contained so the worker
                // remains available for later work.
            }
            finally
            {
                _queue.CompleteProcessing(item.Key);
            }
        }
    }

    private async Task ProcessWithRetryAsync(LibraryWorkItem item, CancellationToken cancellationToken)
    {
        var limits = _configuration.Current.Limits;
        var maxAttempts = WorkRetryPolicy.ComputeAttemptCount(limits);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WorkProcessingResult result;
            try
            {
                result = await _processor.ProcessAsync(item, cancellationToken).ConfigureAwait(false)
                    ?? WorkProcessingResult.Terminal("The work processor returned no result.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // An unclassified processor failure is terminal for this attempt;
                // the worker does not retry an unknown failure.
                result = WorkProcessingResult.Terminal("The work processor failed.");
            }

            if (!result.IsRetryable || attempt == maxAttempts - 1)
            {
                return;
            }

            await Task.Delay(
                WorkRetryPolicy.ComputeBackoff(limits, attempt),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
