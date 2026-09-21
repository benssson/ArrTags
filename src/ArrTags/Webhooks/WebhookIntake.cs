using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Webhooks;

/// <summary>
/// The bounded, non-blocking, coalescing intake for authenticated inbound
/// webhooks. It implements the controller's <see cref="IWebhookIntake"/> submit
/// boundary and owns the bounded rate/coalescing behavior:
/// <list type="bullet">
/// <item>A duplicate, out-of-order, or replayed delivery for the same provider
/// record/file event within the short coalescing window is suppressed before any
/// resolution work is attempted.</item>
/// <item>Pending events are bounded by <see cref="Capacity"/>; overflow drops
/// the new event rather than blocking or growing without bound.</item>
/// <item><see cref="TrySubmit"/> never performs external I/O and never throws
/// for ordinary coalescing or overflow, so it can never block a Jellyfin
/// request.</item>
/// <item>A stopped intake rejects new events so host shutdown stops accepting
/// work before the drain.</item>
/// </list>
/// </summary>
public sealed class WebhookIntake : IWebhookIntake, IDisposable
{
    /// <summary>
    /// The default bounded number of pending webhook events. It is an internal
    /// scheduling bound, not a persisted user limit.
    /// </summary>
    public const int DefaultCapacity = 256;

    /// <summary>
    /// The default short window within which duplicate deliveries coalesce.
    /// </summary>
    public static readonly TimeSpan DefaultCoalescingWindow = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Queue<WebhookEvent> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly WebhookCoalescingWindow _coalescing;
    private readonly int _capacity;
    private int _accepting = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookIntake"/> class with
    /// the default bounds.
    /// </summary>
    public WebhookIntake()
        : this(DefaultCapacity)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookIntake"/> class.
    /// </summary>
    /// <param name="capacity">The bounded pending capacity; a value below one is clamped to one.</param>
    /// <param name="coalescingWindow">The optional duplicate-coalescing window; defaults to five seconds.</param>
    public WebhookIntake(int capacity, TimeSpan? coalescingWindow = null)
    {
        _capacity = capacity < 1 ? 1 : capacity;
        _coalescing = new WebhookCoalescingWindow(
            coalescingWindow ?? DefaultCoalescingWindow,
            _capacity * 4);
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

    /// <summary>
    /// Gets a value indicating whether the intake still accepts new events. It is
    /// cleared on host shutdown so a stopped service is not handed new work.
    /// </summary>
    public bool IsAccepting => Volatile.Read(ref _accepting) != 0;

    /// <inheritdoc />
    public bool TrySubmit(WebhookEvent webhookEvent)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);

        if (!webhookEvent.ProducesReconciliationHint)
        {
            // An unsupported provider event is acknowledged but never consumes
            // bounded intake capacity or produces work.
            return false;
        }

        if (!_coalescing.TryAdmit(webhookEvent.CoalesceKey, DateTimeOffset.UtcNow))
        {
            // A duplicate, out-of-order, or replayed delivery is coalesced into
            // the work the first delivery already produced.
            return false;
        }

        lock (_gate)
        {
            if (Volatile.Read(ref _accepting) == 0)
            {
                return false;
            }

            if (_pending.Count >= _capacity)
            {
                // Bounded overflow: drop rather than block the request thread or
                // grow without bound.
                return false;
            }

            _pending.Enqueue(webhookEvent);
        }

        _signal.Release();
        return true;
    }

    /// <summary>
    /// Waits asynchronously for the next event. The caller owns processing it.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait on host shutdown.</param>
    /// <returns>The next accepted webhook event.</returns>
    /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
    public async Task<WebhookEvent> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    continue;
                }

                return _pending.Dequeue();
            }
        }
    }

    /// <summary>
    /// Starts accepting new events. It is safe to call before the first submit.
    /// </summary>
    public void StartAccepting()
    {
        Interlocked.Exchange(ref _accepting, 1);
    }

    /// <summary>
    /// Stops accepting new events. Idempotent.
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
}
