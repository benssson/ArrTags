using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Providers;

namespace ArrTags.Concurrency;

/// <summary>
/// Enforces the ADR-004 provider concurrency limits around Arr reads. A read
/// acquires the global permit first and the per-connection permit second, so the
/// two limits compose without a lock-order cycle: a task that holds the global
/// permit and waits for a connection permit can always be released by an active
/// read that holds both and completes. Both limits are resolved from the current
/// configuration snapshot on every acquisition, so a replaced snapshot takes
/// effect without rebuilding the singleton. The per-connection limiter set is
/// bounded by the number of distinct configured connections (V1 configures at
/// most one Sonarr and one Radarr connection).
/// </summary>
public sealed class ProviderConcurrencyLimiter : IDisposable
{
    private readonly ConfigurationSnapshotService _configuration;
    private readonly DynamicConcurrencyLimiter _global;
    private readonly ConcurrentDictionary<string, DynamicConcurrencyLimiter> _perConnection =
        new ConcurrentDictionary<string, DynamicConcurrencyLimiter>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderConcurrencyLimiter"/> class.
    /// </summary>
    /// <param name="configuration">The configuration snapshot service supplying the current limits.</param>
    /// <exception cref="ArgumentNullException">The configuration service is <see langword="null"/>.</exception>
    public ProviderConcurrencyLimiter(ConfigurationSnapshotService configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _global = new DynamicConcurrencyLimiter(
            () => _configuration.Current.Limits.ProviderConcurrencyGlobal);
    }

    /// <summary>
    /// Gets the number of currently held global provider permits.
    /// </summary>
    public int GlobalActiveCount => _global.ActiveCount;

    /// <summary>
    /// Acquires the global and per-connection provider permits for one read. The
    /// returned lease releases the connection permit first and the global permit
    /// second; disposing it more than once is safe.
    /// </summary>
    /// <param name="connectionId">The stable, non-secret connection scope.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The held permit lease.</returns>
    /// <exception cref="ArgumentNullException">The connection identifier is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">A wait was cancelled.</exception>
    public async Task<IDisposable> AcquireAsync(ArrConnectionId connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        var globalLease = await _global.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var acquired = false;
        try
        {
            var connectionLimiter = _perConnection.GetOrAdd(connectionId.Value, _ => CreateConnectionLimiter());
            var connectionLease = await connectionLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return new CompositeLease(connectionLease, globalLease);
        }
        finally
        {
            if (!acquired)
            {
                globalLease.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _global.Dispose();
        foreach (var limiter in _perConnection.Values)
        {
            limiter.Dispose();
        }

        _perConnection.Clear();
        GC.SuppressFinalize(this);
    }

    private DynamicConcurrencyLimiter CreateConnectionLimiter()
    {
        return new DynamicConcurrencyLimiter(
            () => _configuration.Current.Limits.ProviderConcurrencyPerConnection);
    }

    private sealed class CompositeLease : IDisposable
    {
        private IDisposable? _global;
        private IDisposable? _connection;

        public CompositeLease(IDisposable connection, IDisposable global)
        {
            _connection = connection;
            _global = global;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _connection, null)?.Dispose();
            Interlocked.Exchange(ref _global, null)?.Dispose();
        }
    }
}
