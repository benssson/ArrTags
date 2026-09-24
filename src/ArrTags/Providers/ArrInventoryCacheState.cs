namespace ArrTags.Providers;

/// <summary>
/// The effective freshness of one cached provider inventory observation set at a
/// point in time. It is derived from the entry's observed time and its computed
/// freshness and bounded last-known-good boundaries, so a consumer can never
/// mistake a bounded stale observation set for a current provider read. Only
/// <see cref="Fresh"/> and <see cref="Stale"/> are usable as current inventory;
/// <see cref="Expired"/> is the bounded last-known-good window ending. The
/// <see cref="Stale"/> value is produced by the elapsed TTL second half, not by a
/// provider failure: the shipped readers cache nothing on a failed library read,
/// and the per-item bounded last-known-good metadata state is what retains
/// last-known-good metadata.
/// </summary>
public enum ArrInventoryCacheState
{
    /// <summary>
    /// A current observation set observed before its freshness boundary.
    /// </summary>
    Fresh,

    /// <summary>
    /// A bounded last-known-good observation set that is no longer current but is
    /// still usable as current until its stale boundary. It is the TTL second-half
    /// state, not a failure marker.
    /// </summary>
    Stale,

    /// <summary>
    /// The bounded last-known-good window ended; the observation set is not
    /// usable as current inventory and must be re-read from the provider.
    /// </summary>
    Expired,
}
