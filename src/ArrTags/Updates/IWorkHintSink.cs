namespace ArrTags.Updates;

/// <summary>
/// The narrow, non-blocking enqueue boundary used by the Jellyfin library-event
/// handlers. A library event validates relevance and hands a bounded
/// <see cref="LibraryWorkHint"/> to this boundary, then returns immediately.
/// Implementations must never perform external I/O, rendering, or image writes,
/// must never block the calling event publisher on queued work, and must never
/// throw for ordinary overflow or coalescing behavior. Task 6.2 owns the full
/// coalescing, single-flight, cancellation, and worker policy built on this
/// boundary.
/// </summary>
public interface IWorkHintSink
{
    /// <summary>
    /// Gets the bounded capacity of the sink.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the current number of pending hints, safe to expose as a diagnostic
    /// queue-depth metric.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Attempts to enqueue a bounded work hint without blocking. A redundant hint
    /// for an item that already has pending work is coalesced, and a hint that
    /// would exceed the bounded capacity is dropped, so overflow can never grow
    /// without bound or push work onto the library-event thread.
    /// </summary>
    /// <param name="hint">The bounded, provider-neutral work hint.</param>
    /// <returns><see langword="true"/> when the hint was accepted; otherwise <see langword="false"/>.</returns>
    bool TryEnqueue(in LibraryWorkHint hint);
}
