namespace ArrTags.Artwork;

/// <summary>
/// The bounded summary of one startup artwork-operation recovery scan. The scan
/// examines at most the configured reconciliation batch and is cancellable, so
/// the counts are a lower bound whenever <see cref="ReachedBatchLimit"/> or
/// <see cref="Cancelled"/> is set. It carries no item identifiers, paths,
/// credentials, or provider payloads.
/// </summary>
public sealed class ArtworkRecoveryScanResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkRecoveryScanResult"/> class.
    /// </summary>
    /// <param name="examined">The number of durable operation records examined.</param>
    /// <param name="recovered">The number of non-terminal operations recovered to a terminal outcome.</param>
    /// <param name="blocked">The number of operations that fail-closed as invalid or inconsistent.</param>
    /// <param name="deferred">The number of operations whose recovery did not reach a terminal outcome.</param>
    /// <param name="alreadyTerminal">The number of operations already terminal before the scan.</param>
    /// <param name="reachedBatchLimit">Whether the scan stopped at the configured batch bound.</param>
    /// <param name="cancelled">Whether the scan stopped because it was cancelled.</param>
    public ArtworkRecoveryScanResult(
        int examined,
        int recovered,
        int blocked,
        int deferred,
        int alreadyTerminal,
        bool reachedBatchLimit,
        bool cancelled)
    {
        Examined = examined;
        Recovered = recovered;
        Blocked = blocked;
        Deferred = deferred;
        AlreadyTerminal = alreadyTerminal;
        ReachedBatchLimit = reachedBatchLimit;
        Cancelled = cancelled;
    }

    /// <summary>
    /// Gets the number of durable operation records examined.
    /// </summary>
    public int Examined { get; }

    /// <summary>
    /// Gets the number of non-terminal operations recovered to a terminal outcome.
    /// </summary>
    public int Recovered { get; }

    /// <summary>
    /// Gets the number of operations that fail-closed as invalid, corrupt, or inconsistent.
    /// </summary>
    public int Blocked { get; }

    /// <summary>
    /// Gets the number of operations whose recovery did not reach a terminal outcome.
    /// </summary>
    public int Deferred { get; }

    /// <summary>
    /// Gets the number of operations already terminal before the scan.
    /// </summary>
    public int AlreadyTerminal { get; }

    /// <summary>
    /// Gets a value indicating whether the scan stopped at the configured batch
    /// bound, meaning additional records may remain for the per-subject gate to
    /// recover lazily.
    /// </summary>
    public bool ReachedBatchLimit { get; }

    /// <summary>
    /// Gets a value indicating whether the scan stopped because it was cancelled.
    /// </summary>
    public bool Cancelled { get; }

    /// <summary>
    /// Gets a value indicating whether the scan was complete, bounded, and fully
    /// resolved: it was not cancelled, did not hit the batch limit, and no record
    /// failed closed or deferred.
    /// </summary>
    public bool IsComplete => !Cancelled && !ReachedBatchLimit && Blocked == 0 && Deferred == 0;
}
