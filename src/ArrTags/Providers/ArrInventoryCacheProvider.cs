using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;

namespace ArrTags.Providers;

/// <summary>
/// Resolves the bounded provider inventory cache (ADR-018) from the active
/// configuration snapshot instead of capturing the bounds once at registration.
/// It mirrors the project's runtime-activation pattern: the bounds are read from
/// the current snapshot, and a replaced snapshot rebuilds the cache with the new
/// TTL, record bound, and byte bound so a changed limit takes effect without
/// rebuilding the singleton. Rebuilding discards the retained observation sets,
/// which is safe because the cache is non-authoritative, in-memory, and
/// repopulated by the next reconciliation read.
/// </summary>
/// <remarks>
/// The provider also owns the per-connection single-flight population gate. On a
/// cold cache, concurrent work items for the same connection serialize through
/// the gate so one library read (and one set of bulk file reads) populates the
/// cache for all of them instead of one read per concurrent item. A caller that
/// waited on the gate re-checks the cache and only populates when the set is
/// still absent. The gate is an async semaphore, so no thread is blocked and no
/// lock is held across provider I/O, and the lease is released on every path —
/// including a provider failure — so a later attempt is never poisoned.
/// </remarks>
public sealed class ArrInventoryCacheProvider
{
    private readonly ConfigurationSnapshotService _configuration;
    private readonly ConcurrentDictionary<ArrConnectionId, SemaphoreSlim> _populationGates = new();
    private readonly object _gate = new();
    private long _resolvedConfigurationVersion = -1;
    private ArrInventoryCache? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrInventoryCacheProvider"/> class.
    /// </summary>
    /// <param name="configuration">The configuration snapshot service supplying the current bounds.</param>
    /// <exception cref="ArgumentNullException">The configuration service is <see langword="null"/>.</exception>
    public ArrInventoryCacheProvider(ConfigurationSnapshotService configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Gets the inventory cache bound to the current configuration snapshot. A
    /// snapshot whose configuration version differs from the resolved one
    /// rebuilds the cache with the new validated bounds.
    /// </summary>
    public ArrInventoryCache Current
    {
        get
        {
            lock (_gate)
            {
                var snapshot = _configuration.Current;
                if (_cache is null || _resolvedConfigurationVersion != snapshot.ConfigurationVersion)
                {
                    _cache = ArrInventoryCache.FromLimits(snapshot.Limits);
                    _resolvedConfigurationVersion = snapshot.ConfigurationVersion;
                }

                return _cache;
            }
        }
    }

    /// <summary>
    /// Discards the retained observation set for one connection on the cache
    /// bound to the current configuration snapshot (ADR-018 clause 3). The cache
    /// is resolved from the current snapshot exactly as <see cref="Current"/>, so
    /// a replaced snapshot's rebuilt cache is invalidated rather than a captured
    /// one. It is the ArrTags-side webhook invalidation surface: it is bounded
    /// (one dictionary removal), secret-free (the connection identifier carries
    /// no credential), and never holds a lock across provider I/O.
    /// </summary>
    /// <param name="connectionId">The non-secret connection scope to discard.</param>
    /// <returns><see langword="true"/> when a retained set was removed.</returns>
    /// <exception cref="ArgumentNullException">The connection identifier is <see langword="null"/>.</exception>
    public bool Invalidate(ArrConnectionId connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        return Current.Invalidate(connectionId);
    }

    /// <summary>
    /// Discards every retained observation set on the cache bound to the current
    /// configuration snapshot (ADR-018 clause 3). It is the bounded
    /// invalidate-all used by the reconciliation sources (Jellyfin library
    /// refresh/post-scan and scheduled/manual reconciliation) and by a webhook
    /// whose connection cannot be resolved; it resolves the current snapshot's
    /// cache and never captures the bounds once at registration.
    /// </summary>
    public void InvalidateAll()
    {
        Current.InvalidateAll();
    }

    /// <summary>
    /// Acquires the per-connection single-flight population gate. Concurrent cold
    /// readers for the same connection serialize here so one population serves
    /// all of them; a caller that waited re-checks the cache and only populates
    /// when the set is still absent. The returned lease must be disposed to
    /// release the gate; disposing it more than once is safe, and the lease is
    /// released even when the population fails or is cancelled.
    /// </summary>
    /// <param name="connectionId">The connection scope whose population is serialized.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the gate.</param>
    /// <returns>The held population lease.</returns>
    /// <exception cref="ArgumentNullException">The connection identifier is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">The wait for the gate was cancelled.</exception>
    public async Task<IDisposable> AcquirePopulationAsync(ArrConnectionId connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        var gate = _populationGates.GetOrAdd(connectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new PopulationLease(gate);
    }

    private sealed class PopulationLease : IDisposable
    {
        private SemaphoreSlim? _gate;

        public PopulationLease(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
