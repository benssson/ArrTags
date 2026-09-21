using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Concurrency;

/// <summary>
/// A bounded, cancellation-aware concurrency limiter whose effective limit is
/// resolved from the current configuration snapshot on every acquisition. The
/// limit may shrink or grow between acquisitions without rebuilding the limiter,
/// so a replaced configuration takes effect without capturing a stale value at
/// singleton construction. A waiter that cannot acquire is suspended on a signal
/// rather than blocking a thread, is cancelled by the supplied token, and the
/// number of waiters is inherently bounded by the callers (the bounded worker
/// pool and the artwork pipeline), so the limiter never grows an unbounded queue
/// of its own.
/// </summary>
public sealed class DynamicConcurrencyLimiter : IDisposable
{
    private readonly object _gate = new object();
    private readonly Func<int> _limitProvider;
    private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
    private int _active;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicConcurrencyLimiter"/> class.
    /// </summary>
    /// <param name="limitProvider">
    /// Supplies the current validated concurrency limit. It is invoked on every
    /// acquisition, so it must read the live configuration snapshot rather than a
    /// captured value. A value below one is clamped to one.
    /// </param>
    /// <exception cref="ArgumentNullException">The provider is <see langword="null"/>.</exception>
    public DynamicConcurrencyLimiter(Func<int> limitProvider)
    {
        _limitProvider = limitProvider ?? throw new ArgumentNullException(nameof(limitProvider));
    }

    /// <summary>
    /// Gets the number of currently held permits.
    /// </summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>
    /// Gets the currently effective limit.
    /// </summary>
    public int CurrentLimit => ResolveLimit();

    /// <summary>
    /// Acquires one permit, waiting asynchronously when the effective limit is
    /// currently reached. The returned lease must be disposed to release the
    /// permit; disposing a lease more than once is safe.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The held permit lease.</returns>
    /// <exception cref="ObjectDisposedException">The limiter was disposed.</exception>
    /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_active < ResolveLimit())
                {
                    _active++;
                    return new Lease(this);
                }
            }

            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _signal.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_active > 0)
            {
                _active--;
            }
        }

        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
            // A release that races with disposal is bounded and safe: the
            // limiter is only disposed during host shutdown.
        }
    }

    private int ResolveLimit()
    {
        var limit = _limitProvider();
        return limit < 1 ? 1 : limit;
    }

    private sealed class Lease : IDisposable
    {
        private DynamicConcurrencyLimiter? _limiter;

        public Lease(DynamicConcurrencyLimiter limiter)
        {
            _limiter = limiter;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _limiter, null)?.Release();
        }
    }
}
