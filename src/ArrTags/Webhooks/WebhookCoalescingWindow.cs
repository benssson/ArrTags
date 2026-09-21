using System;
using System.Collections.Generic;

namespace ArrTags.Webhooks;

/// <summary>
/// A bounded, in-memory, time-windowed coalescing filter for inbound webhook
/// deliveries. It admits a delivery once per coalescing key within a short
/// window and suppresses duplicate, out-of-order, and replayed deliveries of the
/// same provider record/file event before any resolution work is attempted. The
/// filter is bounded by <see cref="Capacity"/>: once the live window is full, the
/// oldest admitted key is evicted so the structure can never grow without bound.
/// It is not a persistent replay/nonce store; work remains idempotent because the
/// worker re-reads current state (ADR-012).
/// </summary>
public sealed class WebhookCoalescingWindow
{
    private readonly object _gate = new();
    private readonly Dictionary<WebhookCoalesceKey, DateTimeOffset> _seen = new();
    private readonly Queue<(WebhookCoalesceKey Key, DateTimeOffset At)> _order = new();
    private readonly TimeSpan _window;
    private readonly int _capacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookCoalescingWindow"/> class.
    /// </summary>
    /// <param name="window">The short window within which a duplicate key is suppressed.</param>
    /// <param name="capacity">The bounded maximum number of live admitted keys.</param>
    /// <exception cref="ArgumentOutOfRangeException">The window is not positive or the capacity is below one.</exception>
    public WebhookCoalescingWindow(TimeSpan window, int capacity)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "A coalescing window must be positive.");
        }

        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A coalescing window capacity must be at least one.");
        }

        _window = window;
        _capacity = capacity;
    }

    /// <summary>
    /// Gets the bounded maximum number of live admitted keys.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Gets the short window within which a duplicate key is suppressed.
    /// </summary>
    public TimeSpan Window => _window;

    /// <summary>
    /// Gets the current number of live admitted keys.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _seen.Count;
            }
        }
    }

    /// <summary>
    /// Attempts to admit a delivery for a coalescing key. A key already admitted
    /// within the window is suppressed and returns <see langword="false"/>.
    /// </summary>
    /// <param name="key">The bounded coalescing key.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the delivery was admitted.</returns>
    public bool TryAdmit(WebhookCoalesceKey key, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);

            if (_seen.TryGetValue(key, out var admittedAt) && now - admittedAt < _window)
            {
                return false;
            }

            if (_seen.Count >= _capacity)
            {
                EvictOldest();
            }

            _seen[key] = now;
            _order.Enqueue((key, now));
            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        while (_order.Count > 0)
        {
            var (key, at) = _order.Peek();
            if (now - at < _window)
            {
                break;
            }

            _order.Dequeue();

            // A newer admission for the same key leaves this stale entry alone.
            if (_seen.TryGetValue(key, out var current) && current == at)
            {
                _seen.Remove(key);
            }
        }
    }

    private void EvictOldest()
    {
        while (_order.Count > 0)
        {
            var (key, at) = _order.Dequeue();
            if (_seen.TryGetValue(key, out var current) && current == at)
            {
                _seen.Remove(key);
                return;
            }
        }

        _seen.Clear();
    }
}
