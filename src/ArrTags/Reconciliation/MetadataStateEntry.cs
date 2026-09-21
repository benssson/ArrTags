using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;

namespace ArrTags.Reconciliation;

/// <summary>
/// The canonical, versioned, restart-safe metadata state record for one Jellyfin
/// item and its resolved provider connection. It carries the match and the
/// last-known-good normalized provider metadata and is persisted through the
/// versioned cache state boundary. It is provider-neutral and never contains a
/// credential, provider DTO, path, or unbounded external payload.
/// </summary>
/// <remarks>
/// The record intentionally omits the freshness policy
/// (<see cref="ExpiresAt"/>/<see cref="StaleUntil"/> transitions and the
/// bounded stale-last-known-good window), which is owned by the later Phase 6
/// freshness task. This task records the fields and publishes current
/// observations only.
/// </remarks>
public sealed class MetadataStateEntry
{
    /// <summary>
    /// The current metadata cache record version. A different version is
    /// rebuildable from provider state and is never treated as current.
    /// </summary>
    public const int CurrentCacheVersion = 1;

    private static readonly IReadOnlyDictionary<string, string> NoProviderIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataStateEntry"/> class.
    /// </summary>
    /// <param name="cacheVersion">The metadata cache record version.</param>
    /// <param name="cacheKey">The opaque, secret-free cache key.</param>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="itemType">The supported structural Jellyfin item type.</param>
    /// <param name="matchStatus">The bounded match status.</param>
    /// <param name="matchMethod">How the match was established.</param>
    /// <param name="matchFingerprint">The deterministic match fingerprint.</param>
    /// <param name="providerKind">The provider family the match is scoped to.</param>
    /// <param name="providerInstanceId">The opaque provider-instance scope.</param>
    /// <param name="connectionId">The stable, non-secret connection scope.</param>
    /// <param name="state">The explicit operational state.</param>
    /// <param name="libraryId">The owning library identifier when known.</param>
    /// <param name="providerIds">The normalized external provider identifiers of the subject.</param>
    /// <param name="title">The display title; candidate or diagnostic data only.</param>
    /// <param name="productionYear">The production year; candidate or diagnostic data only.</param>
    /// <param name="sourceFingerprint">The opaque Jellyfin source fingerprint when known.</param>
    /// <param name="recordIdentity">The connection-scoped typed record/file identity snapshot.</param>
    /// <param name="matchedProviderIds">The provider identifiers that agreed during the match.</param>
    /// <param name="metadata">The last-known-good normalized metadata snapshot.</param>
    /// <param name="metadataFingerprint">The canonical metadata fingerprint when metadata was observed.</param>
    /// <param name="providerVersion">The observed provider version when available.</param>
    /// <param name="providerVersionToken">The observed provider revision token when available.</param>
    /// <param name="fetchedAt">When the metadata observation was fetched.</param>
    /// <param name="expiresAt">The freshness boundary recorded for the later freshness task.</param>
    /// <param name="staleUntil">The bounded last-known-good boundary recorded for the later freshness task.</param>
    /// <param name="lastError">A bounded, redacted, non-secret error summary.</param>
    public MetadataStateEntry(
        int cacheVersion,
        string cacheKey,
        Guid jellyfinItemId,
        MediaItemType itemType,
        MediaMatchStatus matchStatus,
        MediaMatchMethod matchMethod,
        string matchFingerprint,
        ArrProviderKind providerKind,
        string providerInstanceId,
        string connectionId,
        MetadataStateKind state,
        Guid? libraryId = null,
        IReadOnlyDictionary<string, string>? providerIds = null,
        string? title = null,
        int? productionYear = null,
        string? sourceFingerprint = null,
        MetadataRecordIdentity? recordIdentity = null,
        IReadOnlyDictionary<string, string>? matchedProviderIds = null,
        MetadataSnapshot? metadata = null,
        string? metadataFingerprint = null,
        string? providerVersion = null,
        string? providerVersionToken = null,
        DateTimeOffset? fetchedAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? staleUntil = null,
        string? lastError = null)
    {
        CacheVersion = cacheVersion;
        CacheKey = cacheKey ?? string.Empty;
        JellyfinItemId = jellyfinItemId;
        ItemType = itemType;
        MatchStatus = matchStatus;
        MatchMethod = matchMethod;
        MatchFingerprint = matchFingerprint ?? string.Empty;
        ProviderKind = providerKind;
        ProviderInstanceId = providerInstanceId ?? string.Empty;
        ConnectionId = connectionId ?? string.Empty;
        State = state;
        LibraryId = libraryId;
        ProviderIds = providerIds is null
            ? NoProviderIds
            : new Dictionary<string, string>(providerIds, StringComparer.OrdinalIgnoreCase);
        Title = title;
        ProductionYear = productionYear;
        SourceFingerprint = sourceFingerprint;
        RecordIdentity = recordIdentity;
        MatchedProviderIds = matchedProviderIds is null
            ? NoProviderIds
            : new Dictionary<string, string>(matchedProviderIds, StringComparer.OrdinalIgnoreCase);
        Metadata = metadata;
        MetadataFingerprint = metadataFingerprint;
        ProviderVersion = providerVersion;
        ProviderVersionToken = providerVersionToken;
        FetchedAt = fetchedAt;
        ExpiresAt = expiresAt;
        StaleUntil = staleUntil;
        LastError = lastError;
    }

    /// <summary>
    /// Gets the metadata cache record version.
    /// </summary>
    public int CacheVersion { get; }

    /// <summary>
    /// Gets the opaque, secret-free cache key. It includes the Jellyfin item,
    /// provider and connection scope, and the typed record/file identity.
    /// </summary>
    public string CacheKey { get; }

    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public Guid JellyfinItemId { get; }

    /// <summary>
    /// Gets the supported structural Jellyfin item type.
    /// </summary>
    public MediaItemType ItemType { get; }

    /// <summary>
    /// Gets the owning library identifier when known.
    /// </summary>
    public Guid? LibraryId { get; }

    /// <summary>
    /// Gets the normalized external provider identifiers of the subject.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; }

    /// <summary>
    /// Gets the display title; candidate or diagnostic data only.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Gets the production year; candidate or diagnostic data only.
    /// </summary>
    public int? ProductionYear { get; }

    /// <summary>
    /// Gets the opaque Jellyfin source fingerprint when known.
    /// </summary>
    public string? SourceFingerprint { get; }

    /// <summary>
    /// Gets the provider family the match is scoped to.
    /// </summary>
    public ArrProviderKind ProviderKind { get; }

    /// <summary>
    /// Gets the opaque provider-instance scope. It never contains a credential.
    /// </summary>
    public string ProviderInstanceId { get; }

    /// <summary>
    /// Gets the stable, non-secret connection scope.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the bounded match status.
    /// </summary>
    public MediaMatchStatus MatchStatus { get; }

    /// <summary>
    /// Gets how the match was established.
    /// </summary>
    public MediaMatchMethod MatchMethod { get; }

    /// <summary>
    /// Gets the deterministic match fingerprint.
    /// </summary>
    public string MatchFingerprint { get; }

    /// <summary>
    /// Gets the connection-scoped typed record/file identity snapshot, or
    /// <see langword="null"/> when no record matched.
    /// </summary>
    public MetadataRecordIdentity? RecordIdentity { get; }

    /// <summary>
    /// Gets the provider identifiers that agreed during the match.
    /// </summary>
    public IReadOnlyDictionary<string, string> MatchedProviderIds { get; }

    /// <summary>
    /// Gets the last-known-good normalized metadata snapshot, when one was observed.
    /// </summary>
    public MetadataSnapshot? Metadata { get; }

    /// <summary>
    /// Gets the canonical metadata fingerprint, when metadata was observed. It
    /// changes when any badge-affecting value changes.
    /// </summary>
    public string? MetadataFingerprint { get; }

    /// <summary>
    /// Gets the observed provider version when available.
    /// </summary>
    public string? ProviderVersion { get; }

    /// <summary>
    /// Gets the observed provider revision token when available and validated.
    /// Absence is normal.
    /// </summary>
    public string? ProviderVersionToken { get; }

    /// <summary>
    /// Gets when the metadata observation was fetched.
    /// </summary>
    public DateTimeOffset? FetchedAt { get; }

    /// <summary>
    /// Gets the freshness boundary. It is recorded for the later freshness task
    /// and is not computed here.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// Gets the bounded last-known-good boundary. It is recorded for the later
    /// freshness task and is not computed here.
    /// </summary>
    public DateTimeOffset? StaleUntil { get; }

    /// <summary>
    /// Gets the explicit operational state.
    /// </summary>
    public MetadataStateKind State { get; }

    /// <summary>
    /// Gets a bounded, redacted, non-secret error summary when the state is not a
    /// successful observation.
    /// </summary>
    public string? LastError { get; }

    /// <summary>
    /// Creates the canonical metadata state record from a validated match and an
    /// optional normalized metadata snapshot. The state is derived from the match
    /// status and whether metadata was observed.
    /// </summary>
    /// <param name="identity">The current Jellyfin subject identity.</param>
    /// <param name="match">The current match.</param>
    /// <param name="metadata">The normalized metadata when observed.</param>
    /// <param name="fetchedAt">When the observation was made.</param>
    /// <param name="providerVersion">The observed provider version when available.</param>
    /// <param name="providerVersionToken">The observed provider revision token when available.</param>
    /// <returns>The canonical metadata state record.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public static MetadataStateEntry From(
        MediaIdentity identity,
        MediaMatch match,
        BadgeMetadata? metadata,
        DateTimeOffset fetchedAt,
        string? providerVersion = null,
        string? providerVersionToken = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(match);

        if (!identity.JellyfinItemId.Equals(match.MediaIdentity.JellyfinItemId))
        {
            throw new ArgumentException(
                "The match subject must be scoped to the supplied media identity.",
                nameof(match));
        }

        var state = match.Status == MediaMatchStatus.Matched
            ? metadata is null ? MetadataStateKind.Unavailable : MetadataStateKind.Fresh
            : MetadataStateKind.Unmatched;

        return new MetadataStateEntry(
            CurrentCacheVersion,
            ComputeCacheKey(identity, match),
            identity.JellyfinItemId,
            identity.ItemType,
            match.Status,
            match.MatchMethod,
            match.MatchFingerprint,
            match.Provider.Kind,
            match.Provider.ProviderInstanceId,
            match.ConnectionId.Value,
            state,
            libraryId: identity.LibraryId,
            providerIds: identity.ProviderIds,
            title: identity.Title,
            productionYear: identity.ProductionYear,
            sourceFingerprint: identity.SourceFingerprint,
            recordIdentity: match.RecordIdentity is null
                ? null
                : MetadataRecordIdentity.From(match.RecordIdentity),
            matchedProviderIds: match.MatchedProviderIds,
            metadata: metadata is null ? null : MetadataSnapshot.From(metadata),
            metadataFingerprint: metadata?.MetadataFingerprint,
            providerVersion: Bound(providerVersion),
            providerVersionToken: Bound(providerVersionToken),
            fetchedAt: fetchedAt);
    }

    /// <summary>
    /// Computes the opaque, secret-free cache key for a subject and match. The
    /// key includes the Jellyfin item, the provider and connection scope, the
    /// match outcome, and every typed record/file identity component.
    /// </summary>
    /// <param name="identity">The Jellyfin subject identity.</param>
    /// <param name="match">The match.</param>
    /// <returns>A deterministic cache key.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public static string ComputeCacheKey(MediaIdentity identity, MediaMatch match)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(match);

        var builder = new StringBuilder();
        builder.Append("metadata-cache-v").Append(CurrentCacheVersion.ToString(CultureInfo.InvariantCulture)).Append('|');
        builder.Append(identity.JellyfinItemId.ToString("N", CultureInfo.InvariantCulture)).Append('|');
        builder.Append(identity.ItemType.ToString()).Append('|');
        builder.Append(match.Provider.Kind.ToApiName()).Append('|');
        builder.Append(match.Provider.ProviderInstanceId).Append('|');
        builder.Append(match.ConnectionId.Value).Append('|');
        builder.Append(match.Status.ToString()).Append('|');
        builder.Append(match.MatchMethod.ToString()).Append('|');
        builder.Append(match.RecordIdentity?.ToString() ?? "none");
        return builder.ToString();
    }

    /// <summary>
    /// Validates the record's structural invariants. A record that violates them
    /// must not be published.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation when invalid.</param>
    /// <returns><see langword="true"/> when the record is valid.</returns>
    public bool Validate(out string reason)
    {
        if (CacheVersion != CurrentCacheVersion)
        {
            reason = "The metadata cache record has an incompatible cache version.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(CacheKey))
        {
            reason = "The metadata cache record requires a cache key.";
            return false;
        }

        if (JellyfinItemId == Guid.Empty)
        {
            reason = "The metadata cache record requires a Jellyfin item identifier.";
            return false;
        }

        if (!Enum.IsDefined(ItemType))
        {
            reason = "The metadata cache record item type is not defined.";
            return false;
        }

        if (!Enum.IsDefined(ProviderKind))
        {
            reason = "The metadata cache record provider kind is not defined.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProviderInstanceId) || string.IsNullOrWhiteSpace(ConnectionId))
        {
            reason = "The metadata cache record requires a provider and connection scope.";
            return false;
        }

        if (!Enum.IsDefined(MatchStatus) || !Enum.IsDefined(MatchMethod))
        {
            reason = "The metadata cache record match classification is not defined.";
            return false;
        }

        if (!Enum.IsDefined(State))
        {
            reason = "The metadata cache record state is not defined.";
            return false;
        }

        if (RecordIdentity is not null && !RecordIdentity.Validate(out reason))
        {
            return false;
        }

        if (Metadata is not null && !Metadata.Validate(out reason))
        {
            return false;
        }

        switch (State)
        {
            case MetadataStateKind.Fresh:
                if (Metadata is null || string.IsNullOrWhiteSpace(MetadataFingerprint))
                {
                    reason = "A fresh metadata cache record requires metadata and a fingerprint.";
                    return false;
                }

                break;
            case MetadataStateKind.Unmatched:
                if (Metadata is not null || MetadataFingerprint is not null)
                {
                    reason = "An unmatched metadata cache record cannot carry metadata.";
                    return false;
                }

                if (MatchStatus == MediaMatchStatus.Matched)
                {
                    reason = "An unmatched metadata cache record cannot carry a matched status.";
                    return false;
                }

                break;
            case MetadataStateKind.Invalid:
                if (Metadata is not null || MetadataFingerprint is not null)
                {
                    reason = "An invalid metadata cache record cannot carry metadata.";
                    return false;
                }

                break;
        }

        reason = string.Empty;
        return true;
    }

    private static string? Bound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 256 ? trimmed : trimmed[..256];
    }
}
