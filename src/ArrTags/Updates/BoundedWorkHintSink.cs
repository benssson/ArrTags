using System;
using System.Collections.Generic;

namespace ArrTags.Updates;

/// <summary>
/// A minimal bounded, coalescing, non-blocking implementation of the
/// <see cref="IWorkHintSink"/> enqueue boundary. It is sufficient for the task
/// 6.1 library-event entry boundary and is intended to be built upon by the task
/// 6.2 queue. The critical section is a short in-memory dictionary/queue
/// operation with no external call, so a library event that overflows the queue
/// is dropped rather than blocked.
/// </summary>
/// <remarks>
/// Overflow behavior is deliberately simple and safe: a hint for an item that
/// already has a pending hint is coalesced (dropped as redundant), and a hint
/// received at capacity is dropped. Neither path throws or waits. The full
/// coalescing-by-connection, single-flight, cancellation, retry-classification,
/// and worker policy is owned by task 6.2 and is not implemented here.
/// </remarks>
public sealed class BoundedWorkHintSink : IWorkHintSink
{
    private readonly object _gate = new object();
    private readonly int _capacity;
    private readonly Queue<LibraryWorkHint> _pending = new Queue<LibraryWorkHint>();
    private readonly HashSet<Guid> _pendingItemIds = new HashSet<Guid>();

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedWorkHintSink"/> class.
    /// </summary>
    /// <param name="capacity">
    /// The bounded capacity. A value below one is clamped to one so the sink is
    /// always able to accept at least one hint.
    /// </param>
    public BoundedWorkHintSink(int capacity)
    {
        _capacity = capacity < 1 ? 1 : capacity;
    }

    /// <inheritdoc />
    public int Capacity => _capacity;

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <inheritdoc />
    public bool TryEnqueue(in LibraryWorkHint hint)
    {
        if (hint.ItemId == Guid.Empty)
        {
            return false;
        }

        lock (_gate)
        {
            if (_pendingItemIds.Contains(hint.ItemId))
            {
                // Coalesce: the item already has pending work and a worker
                // re-reads current state, so a redundant hint adds nothing.
                return false;
            }

            if (_pending.Count >= _capacity)
            {
                // Bounded overflow: drop the new work rather than block the
                // library-event publisher or grow without bound.
                return false;
            }

            _pending.Enqueue(hint);
            _pendingItemIds.Add(hint.ItemId);
            return true;
        }
    }

    /// <summary>
    /// Attempts to remove the oldest pending hint without blocking. This is a
    /// queue primitive for the task 6.2 worker; no worker or processing pipeline
    /// is implemented by this type.
    /// </summary>
    /// <param name="hint">The oldest pending hint when the queue was not empty.</param>
    /// <returns><see langword="true"/> when a hint was removed.</returns>
    public bool TryDequeue(out LibraryWorkHint hint)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                hint = default;
                return false;
            }

            hint = _pending.Dequeue();
            _pendingItemIds.Remove(hint.ItemId);
            return true;
        }
    }
}
