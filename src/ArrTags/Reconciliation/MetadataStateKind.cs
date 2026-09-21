namespace ArrTags.Reconciliation;

/// <summary>
/// The explicit operational state of one persisted metadata cache record. The
/// state is stored so stale or unavailable data is never mistaken for a current
/// successful snapshot. The freshness policy that transitions
/// <see cref="Fresh"/> to <see cref="Stale"/> is owned by the later Phase 6
/// freshness task; this task records the field and publishes current
/// observations only.
/// </summary>
public enum MetadataStateKind
{
    /// <summary>
    /// The record carries a current, successfully fetched metadata snapshot.
    /// </summary>
    Fresh,

    /// <summary>
    /// The record carries a last-known-good snapshot that is no longer current.
    /// </summary>
    Stale,

    /// <summary>
    /// The item is confirmed not to match a provider record. No metadata is
    /// carried.
    /// </summary>
    Unmatched,

    /// <summary>
    /// A matching record exists but its metadata could not be observed. No
    /// metadata is carried or the last-known-good snapshot is retained.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The record is not usable and must be rebuilt from a fresh observation.
    /// </summary>
    Invalid,
}
