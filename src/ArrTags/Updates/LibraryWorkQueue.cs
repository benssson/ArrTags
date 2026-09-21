using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;

namespace ArrTags.Updates;

/// <summary>
/// The bounded, coalescing, cancellation-aware update work queue. It implements
/// the task 6.1 <see cref="IWorkHintSink"/> enqueue boundary and adds the task
/// 6.2 queue mechanics:
/// <list type="bullet">
/// <item>Work is coalesced by item, resolved connection, and image surface. A
/// redundant hint for work that is already pending or in flight is dropped
/// rather than duplicated.</item>
/// <item>At most <see cref="OperationalLimits.PerItemInFlightWork"/> work item
/// per key is in flight at once (the ADR-004 validation range is exactly
/// one).</item>
/// <item>The pending bound is the ADR-004 <see cref="OperationalLimits.QueueCapacity"/>,
/// resolved from the current configuration snapshot on every enqueue so a
/// replaced snapshot takes effect without rebuilding the singleton.</item>
/// <item>Overflow coalesces or drops redundant work. Enqueue never waits on
/// external work, so it can never block the Jellyfin library-event
/// publisher.</item>
/// <item>A stopped queue rejects new work so host shutdown stops accepting
/// work before the drain.</item>
/// </list>
/// </summary>
#pragma warning disable CA1711 // The architecture names this component the bounded work queue.
public sealed class LibraryWorkQueue : IWorkHintSink, IDisposable
{
    private readonly object _gate = new object();
    private readonly Queue<LibraryWorkItem> _pending = new Queue<LibraryWorkItem>();
    private readonly HashSet<WorkItemKey> _pendingKeys = new HashSet<WorkItemKey>();
    private readonly Dictionary<WorkItemKey, int> _inFlight = new Dictionary<WorkItemKey, int>();
    private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
    private readonly Func<OperationalLimits> _limits;
    private int _accepting = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWorkQueue"/> class
    /// whose capacity and per-item in-flight bound are resolved from the current
    /// configuration snapshot on each operation.
    /// </summary>
    /// <param name="limitsProvider">Supplies the current validated operational limits.</param>
    /// <exception cref="ArgumentNullException">The provider is <see langword="null"/>.</exception>
    public LibraryWorkQueue(Func<OperationalLimits> limitsProvider)
    {
        _limits = limitsProvider ?? throw new ArgumentNullException(nameof(limitsProvider));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWorkQueue"/> class
    /// with a fixed pending capacity. This convenience overload is intended for
    /// focused tests and simple hosts; production registration supplies the
    /// configuration snapshot provider.
    /// </summary>
    /// <param name="capacity">
    /// The bounded capacity. A value below one is clamped to one so the queue is
    /// always able to accept at least one item.
    /// </param>
    public LibraryWorkQueue(int capacity)
    {
        var limits = new OperationalLimits
        {
            QueueCapacity = capacity < 1 ? 1 : capacity,
        };

        _limits = () => limits;
    }

    /// <inheritdoc />
    public int Capacity
    {
        get
        {
            var capacity = _limits().QueueCapacity;
            return capacity < 1 ? 1 : capacity;
        }
    }

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

    /// <summary>
    /// Gets the number of work items currently held by a worker for processing.
    /// </summary>
    public int InFlightCount
    {
        get
        {
            lock (_gate)
            {
                return _inFlight.Count;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the queue still accepts new work. It is
    /// cleared on host shutdown so a stopped worker cannot be handed new work.
    /// </summary>
    public bool IsAccepting => Volatile.Read(ref _accepting) != 0;

    /// <inheritdoc />
    public bool TryEnqueue(in LibraryWorkHint hint)
    {
        if (hint.ItemId == Guid.Empty)
        {
            return false;
        }

        return TryEnqueue(LibraryWorkItem.FromHint(in hint));
    }

    /// <summary>
    /// Attempts to enqueue a bounded, connection-scoped work item without
    /// blocking. A redundant item for a key that already has pending or
    /// in-flight work is coalesced, and an item that would exceed the bounded
    /// capacity is dropped, so overflow can never grow without bound or push
    /// work onto the calling thread.
    /// </summary>
    /// <param name="item">The bounded queued work item.</param>
    /// <returns><see langword="true"/> when the item was accepted; otherwise <see langword="false"/>.</returns>
    public bool TryEnqueue(in LibraryWorkItem item)
    {
        if (item.Key.ItemId == Guid.Empty || item.Key.Surface is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (Volatile.Read(ref _accepting) == 0)
            {
                // A stopped queue rejects new work so host shutdown stops
                // accepting work before the lifecycle drain.
                return false;
            }

            var perItemInFlight = ResolvePerItemInFlight();

            if (_inFlight.TryGetValue(item.Key, out var inFlight) && inFlight >= perItemInFlight)
            {
                // A worker re-reads current item and configuration state, so a
                // redundant hint for in-flight work adds nothing.
                return false;
            }

            if (_pendingKeys.Contains(item.Key))
            {
                // Coalesce: the item already has pending work.
                return false;
            }

            if (_pending.Count >= Capacity)
            {
                // Bounded overflow: drop the new work rather than block the
                // library-event publisher or grow without bound.
                return false;
            }

            _pending.Enqueue(item);
            _pendingKeys.Add(item.Key);
        }

        _signal.Release();
        return true;
    }

    /// <summary>
    /// Waits asynchronously for the next work item, marks its key in flight, and
    /// returns it. The caller must invoke <see cref="CompleteProcessing"/> when
    /// processing completes.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait on host shutdown.</param>
    /// <returns>The next queued work item.</returns>
    /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
    public async Task<LibraryWorkItem> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    // Defensive: a signal without a pending item. Wait again.
                    continue;
                }

                var item = _pending.Dequeue();
                _pendingKeys.Remove(item.Key);

                _inFlight.TryGetValue(item.Key, out var inFlight);
                _inFlight[item.Key] = inFlight + 1;
                return item;
            }
        }
    }

    /// <summary>
    /// Releases the in-flight slot for a processed work item.
    /// </summary>
    /// <param name="key">The key returned by <see cref="DequeueAsync"/>.</param>
    public void CompleteProcessing(in WorkItemKey key)
    {
        lock (_gate)
        {
            if (!_inFlight.TryGetValue(key, out var inFlight))
            {
                return;
            }

            if (inFlight <= 1)
            {
                _inFlight.Remove(key);
            }
            else
            {
                _inFlight[key] = inFlight - 1;
            }
        }
    }

    /// <summary>
    /// Starts accepting new work. It is safe to call before the first enqueue.
    /// </summary>
    public void StartAccepting()
    {
        Interlocked.Exchange(ref _accepting, 1);
    }

    /// <summary>
    /// Stops accepting new work. Idempotent.
    /// </summary>
    public void StopAccepting()
    {
        Interlocked.Exchange(ref _accepting, 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _signal.Dispose();
        GC.SuppressFinalize(this);
    }

    private int ResolvePerItemInFlight()
    {
        var value = _limits().PerItemInFlightWork;
        return value < 1 ? 1 : value;
    }
}
#pragma warning restore CA1711
