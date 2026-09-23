using System;
using System.Collections.Generic;

namespace ArrTags.Providers;

/// <summary>
/// The canonical, secret-free, in-memory observation set for one
/// <see cref="ArrConnection"/> held by the provider inventory cache (ADR-018
/// clauses 1, 5, and 6). It carries the provider library list and the per-record
/// file observations as canonical observations, plus the computed freshness and
/// bounded last-known-good boundaries.
/// </summary>
/// <remarks>
/// The entry is non-authoritative: it is never persisted, is rebuilt empty on
/// restart, and never contains a credential, secret lease, provider DTO, or
/// Jellyfin item state. The entry's only free-text carrier is
/// <see cref="LastError"/>, a bounded <see cref="ArrProviderError"/>. The cache
/// stores that value unchanged and does not perform value-level redaction of its
/// message: value-level redaction is the producer contract
/// (<see cref="ArrProviderError"/> is documented as bounded and redacted,
/// ADR-020 clause 4), exactly as for the per-item metadata record's last-error
/// summary. The entry therefore never stores the <see cref="ArrConnection"/>, its
/// API-key reference, or a secret lease. It is distinct from the per-item
/// <c>MetadataCacheEntry</c> (<c>docs/data-model.md</c> 3.9), which remains the
/// per-item match and metadata freshness record. The configured inventory TTL is
/// the total bounded lifetime of the observation set: it is fresh for the first
/// half (<see cref="ExpiresAt"/>) and may be served as explicit bounded
/// last-known-good for the remaining half until <see cref="StaleUntil"/>; at or
/// after <see cref="StaleUntil"/> it is expired and unusable as current.
/// </remarks>
public sealed class ArrInventoryCacheEntry
{
    /// <summary>
    /// The current inventory cache record version. A different version is
    /// rebuildable from provider state and is never treated as current.
    /// </summary>
    public const int CurrentCacheVersion = 1;

    private const long RecordOverheadBytes = 256L;
    private const long FileObservationOverheadBytes = 512L;

    private static readonly IReadOnlyList<ArrInventoryRecordObservation> NoRecords =
        Array.Empty<ArrInventoryRecordObservation>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrInventoryCacheEntry"/> class.
    /// </summary>
    /// <param name="cacheVersion">The inventory cache record version.</param>
    /// <param name="connectionId">The stable, non-secret connection scope.</param>
    /// <param name="providerKind">The provider family the observation set is scoped to.</param>
    /// <param name="observedAt">When the observation set was captured.</param>
    /// <param name="ttl">The total bounded lifetime of the observation set.</param>
    /// <param name="records">The canonical record and file observations.</param>
    /// <param name="lastError">A bounded, redacted, non-secret failure summary when a refresh failed but last-known-good was retained.</param>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The provider kind is undefined or the TTL is not positive.</exception>
    public ArrInventoryCacheEntry(
        int cacheVersion,
        ArrConnectionId connectionId,
        ArrProviderKind providerKind,
        DateTimeOffset observedAt,
        TimeSpan ttl,
        IReadOnlyList<ArrInventoryRecordObservation> records,
        ArrProviderError? lastError = null)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(records);

        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind), providerKind, "Unknown Arr provider kind.");
        }

        var (expiresAt, staleUntil) = ComputeFreshnessWindow(observedAt, ttl);

        CacheVersion = cacheVersion;
        ConnectionId = connectionId;
        ProviderKind = providerKind;
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        StaleUntil = staleUntil;
        Records = records.Count == 0
            ? NoRecords
            : new List<ArrInventoryRecordObservation>(records).AsReadOnly();
        LastError = lastError;
        SizeBytes = EstimateSizeBytes(Records);
    }

    /// <summary>
    /// Gets the inventory cache record version.
    /// </summary>
    public int CacheVersion { get; }

    /// <summary>
    /// Gets the stable, non-secret connection scope.
    /// </summary>
    public ArrConnectionId ConnectionId { get; }

    /// <summary>
    /// Gets the provider family the observation set is scoped to.
    /// </summary>
    public ArrProviderKind ProviderKind { get; }

    /// <summary>
    /// Gets when the observation set was captured.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>
    /// Gets the freshness boundary. Before it, the observation set is
    /// <see cref="ArrInventoryCacheState.Fresh"/>; at or after it, it is a
    /// last-known-good observation set that is still usable until
    /// <see cref="StaleUntil"/>.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets the bounded last-known-good boundary. At or after it, the
    /// observation set is expired and must not be treated as current inventory.
    /// </summary>
    public DateTimeOffset StaleUntil { get; }

    /// <summary>
    /// Gets the canonical record and file observations.
    /// </summary>
    public IReadOnlyList<ArrInventoryRecordObservation> Records { get; }

    /// <summary>
    /// Gets a bounded, redacted, non-secret failure summary when a refresh failed
    /// but the last-known-good observation set was retained; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public ArrProviderError? LastError { get; }

    /// <summary>
    /// Gets the deterministic, conservative estimate of the canonical bytes
    /// retained by this entry, used to enforce the configured per-connection byte
    /// bound. It is an accounting estimate, not an exact serialized size.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// Creates the canonical inventory cache entry from a resolved connection.
    /// </summary>
    /// <param name="connection">The resolved connection the observation set is scoped to.</param>
    /// <param name="observedAt">When the observation set was captured.</param>
    /// <param name="ttl">The total bounded lifetime of the observation set.</param>
    /// <param name="records">The canonical record and file observations.</param>
    /// <param name="lastError">A bounded, redacted, non-secret failure summary when applicable.</param>
    /// <returns>The canonical inventory cache entry.</returns>
    /// <exception cref="ArgumentNullException">The connection is <see langword="null"/>.</exception>
    public static ArrInventoryCacheEntry From(
        ArrConnection connection,
        DateTimeOffset observedAt,
        TimeSpan ttl,
        IReadOnlyList<ArrInventoryRecordObservation> records,
        ArrProviderError? lastError = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new ArrInventoryCacheEntry(
            CurrentCacheVersion,
            connection.ConnectionId,
            connection.Provider.Kind,
            observedAt,
            ttl,
            records,
            lastError);
    }

    /// <summary>
    /// Computes the freshness and bounded last-known-good boundaries for an
    /// observation time and a configured TTL. The configured TTL is the total
    /// bounded lifetime: the observation set is fresh for the first half and may
    /// be retained as bounded last-known-good for the remaining half, so the
    /// total never exceeds the configured TTL.
    /// </summary>
    /// <param name="observedAt">When the observation was made.</param>
    /// <param name="ttl">The configured total bounded lifetime.</param>
    /// <returns>The computed freshness and last-known-good boundaries.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The TTL is not positive.</exception>
    public static (DateTimeOffset ExpiresAt, DateTimeOffset StaleUntil) ComputeFreshnessWindow(
        DateTimeOffset observedAt,
        TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "An inventory cache TTL must be positive.");
        }

        var freshnessWindow = ttl / 2;
        if (freshnessWindow < TimeSpan.FromTicks(1))
        {
            // A degenerate sub-tick window collapses the freshness half onto the
            // full bound so the total bound stays positive and correctly ordered.
            freshnessWindow = ttl;
        }

        return (observedAt + freshnessWindow, observedAt + ttl);
    }

    /// <summary>
    /// Evaluates the effective freshness of the observation set at a point in
    /// time. An entry past its stale boundary is
    /// <see cref="ArrInventoryCacheState.Expired"/> and must not be treated as
    /// current.
    /// </summary>
    /// <param name="now">The evaluation time.</param>
    /// <returns>The effective freshness.</returns>
    public ArrInventoryCacheState EvaluateState(DateTimeOffset now)
    {
        if (IsExpired(now))
        {
            return ArrInventoryCacheState.Expired;
        }

        return now < ExpiresAt ? ArrInventoryCacheState.Fresh : ArrInventoryCacheState.Stale;
    }

    /// <summary>
    /// Determines whether the observation set may be treated as current
    /// inventory at a point in time. Only a fresh or bounded stale
    /// last-known-good set is usable; an expired set never is.
    /// </summary>
    /// <param name="now">The evaluation time.</param>
    /// <returns><see langword="true"/> when the set is usable as current.</returns>
    public bool IsUsableAsCurrent(DateTimeOffset now)
    {
        return !IsExpired(now);
    }

    /// <summary>
    /// Determines whether the bounded last-known-good window has ended.
    /// </summary>
    /// <param name="now">The evaluation time.</param>
    /// <returns><see langword="true"/> when the set is past its stale boundary.</returns>
    public bool IsExpired(DateTimeOffset now)
    {
        return now >= StaleUntil;
    }

    /// <summary>
    /// Creates a copy of this entry that records a bounded, redacted provider
    /// failure while preserving the observation set and its computed boundaries
    /// unchanged, so a temporary outage can keep the last-known-good inventory
    /// but can never extend the bounded window.
    /// </summary>
    /// <param name="error">The bounded, redacted provider failure.</param>
    /// <returns>The last-known-good entry annotated with the failure.</returns>
    /// <exception cref="ArgumentNullException">The error is <see langword="null"/>.</exception>
    public ArrInventoryCacheEntry WithFailure(ArrProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ArrInventoryCacheEntry(
            CacheVersion,
            ConnectionId,
            ProviderKind,
            ObservedAt,
            StaleUntil - ObservedAt,
            Records,
            error);
    }

    /// <summary>
    /// Validates the entry field invariants. A valid entry carries a known cache
    /// version and provider kind, a bounded TTL, and only observations scoped to
    /// its connection and provider.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation when invalid.</param>
    /// <returns><see langword="true"/> when the entry is valid.</returns>
    public bool Validate(out string reason)
    {
        if (CacheVersion <= 0)
        {
            reason = "The inventory cache entry version must be positive.";
            return false;
        }

        if (!Enum.IsDefined(ProviderKind))
        {
            reason = "The inventory cache entry provider kind is not defined.";
            return false;
        }

        if (StaleUntil <= ObservedAt)
        {
            reason = "The inventory cache entry requires a positive bounded lifetime.";
            return false;
        }

        foreach (var record in Records)
        {
            if (record is null)
            {
                reason = "The inventory cache entry contains an empty record observation.";
                return false;
            }

            if (!record.Candidate.ConnectionId.Equals(ConnectionId)
                || record.Candidate.ProviderKind != ProviderKind)
            {
                reason = "The inventory cache entry contains an observation outside its connection or provider scope.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static long EstimateSizeBytes(IReadOnlyList<ArrInventoryRecordObservation> records)
    {
        long total = 0;
        foreach (var record in records)
        {
            total += RecordOverheadBytes;
            total += StringBytes(record.Candidate.Title);
            total += StringBytes(record.Candidate.PrimaryPath);
            foreach (var pair in record.Candidate.ProviderIds)
            {
                total += StringBytes(pair.Key) + StringBytes(pair.Value);
            }

            if (record.FileObservation is { } file)
            {
                total += FileObservationOverheadBytes;
                total += StringBytes(file.Quality?.Label);
                total += StringBytes(file.Quality?.Source);
                total += StringBytes(file.Quality?.Modifier);
                total += StringBytes(file.Resolution?.Label);
                total += StringBytes(file.DynamicRange?.Profile);
                total += StringBytes(file.VideoCodec);
                total += StringBytes(file.AudioCodec);
                total += StringBytes(file.Source);
                foreach (var badge in file.CustomBadges)
                {
                    total += StringBytes(badge);
                }

                foreach (var pair in file.Extensions)
                {
                    total += StringBytes(pair.Key) + StringBytes(pair.Value);
                }
            }
        }

        return total;
    }

    private static long StringBytes(string? value)
    {
        return value is null ? 0 : value.Length * 2L;
    }
}
