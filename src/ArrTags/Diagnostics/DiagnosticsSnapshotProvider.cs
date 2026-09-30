using System;
using ArrTags.Updates;

namespace ArrTags.Diagnostics;

/// <summary>
/// The read-only diagnostics snapshot provider. It assembles the fixed-shape,
/// bounded, secret-free <see cref="DiagnosticsSnapshot"/> from the
/// process-lifetime <see cref="DiagnosticsMetrics"/> counters and the existing
/// bounded work-queue depth and in-flight properties, so the future
/// administrator status endpoint (ADR-025 clause 1) can resolve one service and
/// serve the snapshot without blocking on provider, render, or library work.
/// </summary>
/// <remarks>
/// The provider is a stateless singleton. <see cref="GetSnapshot"/> reads only
/// in-memory counters and the queue's existing bounded, lock-protected depth and
/// in-flight values, and performs no I/O and no work that can wait on external
/// systems. The queue depth and in-flight values are independent point-in-time
/// observations of bounded integers, not a transactional pair.
/// </remarks>
public sealed class DiagnosticsSnapshotProvider
{
    private readonly DiagnosticsMetrics _metrics;
    private readonly LibraryWorkQueue _queue;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsSnapshotProvider"/> class.
    /// </summary>
    /// <param name="metrics">The process-lifetime diagnostics counters.</param>
    /// <param name="queue">The bounded update work queue.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public DiagnosticsSnapshotProvider(DiagnosticsMetrics metrics, LibraryWorkQueue queue)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>
    /// Captures the current bounded, secret-free diagnostics snapshot. It
    /// resolves the queue depth and in-flight count through the queue's existing
    /// bounded properties and copies the process-lifetime counters; it performs
    /// no provider, render, or library work.
    /// </summary>
    /// <returns>The current fixed-shape diagnostics snapshot.</returns>
    public DiagnosticsSnapshot GetSnapshot()
    {
        return _metrics.Capture(_queue.Count, _queue.InFlightCount);
    }
}
