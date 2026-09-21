namespace ArrTags.Reconciliation;

/// <summary>
/// The effective freshness of one persisted metadata state record at a point in
/// time. It is derived from the record's explicit persisted state and its
/// computed <c>expiresAt</c>/<c>staleUntil</c> boundaries, so a consumer can
/// never mistake stale last-known-good metadata for a current observation.
/// </summary>
/// <remarks>
/// <see cref="Fresh"/> and <see cref="Stale"/> are the only values that are
/// usable as current metadata. <see cref="Expired"/> is the bounded
/// last-known-good window ending: after it, the record must not be treated as
/// current and new artwork publication must stop or retain the current usable
/// artwork. The remaining values mirror the persisted
/// <see cref="MetadataStateKind"/> vocabulary for negative or unusable records.
/// </remarks>
public enum MetadataFreshness
{
    /// <summary>
    /// A current, successfully fetched metadata snapshot observed before its
    /// freshness boundary.
    /// </summary>
    Fresh,

    /// <summary>
    /// A bounded last-known-good snapshot that is no longer current but is still
    /// usable as current until its stale boundary.
    /// </summary>
    Stale,

    /// <summary>
    /// The bounded last-known-good window ended; the record is not usable as
    /// current metadata.
    /// </summary>
    Expired,

    /// <summary>
    /// The item is confirmed not to match a provider record. No metadata is
    /// carried.
    /// </summary>
    Unmatched,

    /// <summary>
    /// A matching record exists but no usable metadata or last-known-good
    /// snapshot is carried.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The record is not usable and must be rebuilt from a fresh observation.
    /// </summary>
    Invalid,
}
