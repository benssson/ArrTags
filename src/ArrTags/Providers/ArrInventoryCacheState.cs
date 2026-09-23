namespace ArrTags.Providers;

/// <summary>
/// The effective freshness of one cached provider inventory observation set at a
/// point in time. It is derived from the entry's observed time and its computed
/// freshness and bounded last-known-good boundaries, so a consumer can never
/// mistake a bounded stale observation set for a current provider read. Only
/// <see cref="Fresh"/> and <see cref="Stale"/> are usable as current inventory;
/// <see cref="Expired"/> is the bounded last-known-good window ending.
/// </summary>
public enum ArrInventoryCacheState
{
    /// <summary>
    /// A current observation set observed before its freshness boundary.
    /// </summary>
    Fresh,

    /// <summary>
    /// A bounded last-known-good observation set that is no longer current but is
    /// still usable as current until its stale boundary.
    /// </summary>
    Stale,

    /// <summary>
    /// The bounded last-known-good window ended; the observation set is not
    /// usable as current inventory and must be re-read from the provider.
    /// </summary>
    Expired,
}
