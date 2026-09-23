using System;
using System.Collections.Generic;
using ArrTags.Configuration;

namespace ArrTags.Providers;

/// <summary>
/// The bounded, in-memory, per-connection provider inventory cache boundary
/// (ADR-018 clauses 1, 5, and 6). It holds one canonical, secret-free observation
/// set per <see cref="ArrConnection"/> so one provider library read can serve a
/// reconciliation window instead of one read per work item.
/// </summary>
/// <remarks>
/// The cache is non-authoritative: it is never persisted, is rebuilt empty on
/// restart, and never contains a credential or secret lease. An observation set
/// that exceeds the configured record or byte bound is rejected and the caller
/// falls back to the direct provider read unchanged. A provider failure keeps
/// the bounded last-known-good observation set until the configured TTL and
/// never extends it. The only free-text carrier is the bounded
/// <see cref="ArrProviderError"/> supplied as <c>lastError</c>; the boundary does
/// not add value-level redaction and relies on that producer contract
/// (<see cref="ArrProviderError"/> is documented as bounded and redacted,
/// ADR-020 clause 4), consistent with the per-item metadata record's last-error
/// summary. The provider clients, metadata readers, and invalidation sources are
/// wired in later work; this boundary defines and enforces only the shape and its
/// bounds.
/// </remarks>
public sealed class ArrInventoryCache
{
    private readonly object _gate = new();
    private readonly Dictionary<ArrConnectionId, ArrInventoryCacheEntry> _entries = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrInventoryCache"/> class.
    /// </summary>
    /// <param name="maxRecordsPerConnection">The maximum record observations retained per connection.</param>
    /// <param name="maxBytesPerConnection">The maximum estimated canonical bytes retained per connection.</param>
    /// <param name="ttl">The total bounded lifetime of one cached observation set.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is not positive.</exception>
    public ArrInventoryCache(int maxRecordsPerConnection, long maxBytesPerConnection, TimeSpan ttl)
    {
        if (maxRecordsPerConnection < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRecordsPerConnection),
                maxRecordsPerConnection,
                "An inventory cache record bound must be positive.");
        }

        if (maxBytesPerConnection < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytesPerConnection),
                maxBytesPerConnection,
                "An inventory cache byte bound must be positive.");
        }

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "An inventory cache TTL must be positive.");
        }

        MaxRecordsPerConnection = maxRecordsPerConnection;
        MaxBytesPerConnection = maxBytesPerConnection;
        Ttl = ttl;
    }

    /// <summary>
    /// Gets the maximum record observations retained per connection.
    /// </summary>
    public int MaxRecordsPerConnection { get; }

    /// <summary>
    /// Gets the maximum estimated canonical bytes retained per connection.
    /// </summary>
    public long MaxBytesPerConnection { get; }

    /// <summary>
    /// Gets the total bounded lifetime of one cached observation set.
    /// </summary>
    public TimeSpan Ttl { get; }

    /// <summary>
    /// Gets the number of connections with a retained observation set.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Creates a cache from the validated operational limits (ADR-018 clause 5).
    /// </summary>
    /// <param name="limits">The validated operational limits.</param>
    /// <returns>The bounded in-memory inventory cache.</returns>
    /// <exception cref="ArgumentNullException">The limits are <see langword="null"/>.</exception>
    public static ArrInventoryCache FromLimits(OperationalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return new ArrInventoryCache(
            limits.InventoryCacheMaxRecords,
            limits.InventoryCacheMaxBytes,
            TimeSpan.FromMinutes(limits.InventoryCacheTtlMinutes));
    }

    /// <summary>
    /// Stores the canonical observation set for one connection, replacing any
    /// retained set. An observation set that exceeds the configured record or
    /// byte bound is not cached and the caller uses the direct provider read
    /// unchanged.
    /// </summary>
    /// <param name="connection">The resolved connection the observation set is scoped to.</param>
    /// <param name="observedAt">When the observation set was captured.</param>
    /// <param name="records">The canonical record and file observations.</param>
    /// <param name="lastError">A bounded, non-secret failure summary; value-level redaction is the producer's contract, the same bounded/redacted <see cref="ArrProviderError"/> contract as the per-item metadata record's last-error summary (ADR-020 clause 4).</param>
    /// <returns><see langword="true"/> when the set was cached.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public bool TryStore(
        ArrConnection connection,
        DateTimeOffset observedAt,
        IReadOnlyList<ArrInventoryRecordObservation> records,
        ArrProviderError? lastError = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(records);

        var entry = ArrInventoryCacheEntry.From(connection, observedAt, Ttl, records, lastError);
        if (!entry.Validate(out _)
            || entry.Records.Count > MaxRecordsPerConnection
            || entry.SizeBytes > MaxBytesPerConnection)
        {
            return false;
        }

        lock (_gate)
        {
            _entries[entry.ConnectionId] = entry;
        }

        return true;
    }

    /// <summary>
    /// Gets the retained observation set for one connection when it is still
    /// usable as current inventory. An absent or expired set returns
    /// <see langword="false"/>; an expired set is evicted.
    /// </summary>
    /// <param name="connectionId">The connection scope to look up.</param>
    /// <param name="now">The evaluation time.</param>
    /// <param name="entry">The retained fresh or bounded stale last-known-good set when found.</param>
    /// <returns><see langword="true"/> when a usable observation set was found.</returns>
    /// <exception cref="ArgumentNullException">The connection identifier is <see langword="null"/>.</exception>
    public bool TryGet(ArrConnectionId connectionId, DateTimeOffset now, out ArrInventoryCacheEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        lock (_gate)
        {
            if (!_entries.TryGetValue(connectionId, out var stored))
            {
                entry = null;
                return false;
            }

            if (stored.IsExpired(now))
            {
                _entries.Remove(connectionId);
                entry = null;
                return false;
            }

            entry = stored;
            return true;
        }
    }
}
