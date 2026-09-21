using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.Updates;

namespace ArrTags.Tests;

/// <summary>
/// A work processor double that blocks until its cancellation token fires. It
/// records how many items started and how many observed cancellation, so a
/// shutdown test can assert that queued work is never started after stop and that
/// in-flight work is genuinely cancelled.
/// </summary>
internal sealed class Phase6BlockingProcessor : IWorkItemProcessor
{
    private int _started;
    private int _cancelled;

    /// <summary>Gets the number of items that entered processing.</summary>
    public int Started => Volatile.Read(ref _started);

    /// <summary>Gets the number of items that observed cancellation.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    /// <summary>Gets a signal set when the first item enters processing.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _started);
        Entered.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }

        return WorkProcessingResult.Completed();
    }
}

/// <summary>
/// A configurable, thread-safe work processor double that records the number of
/// processing calls and the peak per-key concurrency. It is used by the
/// duplicate-event and queue-pressure tests to prove coalescing, single-flight,
/// and bounded work.
/// </summary>
internal sealed class Phase6RecordingProcessor : IWorkItemProcessor
{
    private readonly object _gate = new();
    private readonly Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>> _handler;
    private readonly Dictionary<WorkItemKey, int> _inFlight = new();
    private readonly Dictionary<WorkItemKey, int> _peak = new();
    private int _calls;

    /// <summary>
    /// Initializes a new instance of the <see cref="Phase6RecordingProcessor"/> class.
    /// </summary>
    /// <param name="handler">The per-item handler; a completed result when omitted.</param>
    public Phase6RecordingProcessor(
        Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>>? handler = null)
    {
        _handler = handler ?? (static (_, _) => Task.FromResult(WorkProcessingResult.Completed()));
    }

    /// <summary>Gets the number of processing calls.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _calls++;
            _inFlight.TryGetValue(item.Key, out var current);
            current++;
            _inFlight[item.Key] = current;
            _peak.TryGetValue(item.Key, out var peak);
            if (current > peak)
            {
                _peak[item.Key] = current;
            }
        }

        try
        {
            return await _handler(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight[item.Key] = _inFlight[item.Key] - 1;
            }
        }
    }

    /// <summary>
    /// Gets the peak observed concurrency for a key.
    /// </summary>
    /// <param name="key">The work item key.</param>
    /// <returns>The peak concurrency.</returns>
    public int PeakConcurrency(WorkItemKey key)
    {
        lock (_gate)
        {
            return _peak.TryGetValue(key, out var peak) ? peak : 0;
        }
    }
}

/// <summary>
/// A provider-neutral metadata reader double whose handler is asynchronous, so a
/// cancellation test can block a provider read on its cancellation token.
/// </summary>
internal sealed class Phase6BlockingReader : IArrMetadataReader
{
    /// <summary>Gets or sets the provider family.</summary>
    public ArrProviderKind Kind { get; set; } = ArrProviderKind.Radarr;

    /// <summary>
    /// Gets or sets the asynchronous read handler.
    /// </summary>
    public Func<MediaIdentity, ArrConnection, CancellationToken, Task<ArrMetadataReadResult>> Handler { get; set; } =
        static (_, _, _) => throw new InvalidOperationException("No reader handler was configured.");

    /// <summary>Gets a signal set when a read enters the handler.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public async Task<ArrMetadataReadResult> ReadAsync(
        MediaIdentity identity,
        ArrConnection connection,
        CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        return await Handler(identity, connection, cancellationToken).ConfigureAwait(false);
    }
}
