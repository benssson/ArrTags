namespace ArrTags.Artwork;

/// <summary>
/// The bounded outcome of one reconciliation attempt for one item/image surface.
/// Every value other than <see cref="Completed"/> and <see cref="CommittedFinalState"/>
/// means the active artwork was left unchanged or was adopted only when the
/// durable postcondition proved it; a non-terminal operation is never blindly
/// replayed or cleaned up.
/// </summary>
public enum ArtworkReconciliationOutcome
{
    /// <summary>There was no reconcilable operation; nothing was changed.</summary>
    NothingToReconcile,

    /// <summary>The deterministic operation was resumed and reached a terminal outcome.</summary>
    Resumed,

    /// <summary>The operation was safely aborted because the lifecycle fence refused new mutation work.</summary>
    Aborted,

    /// <summary>The intended after identity was observed and the final state was committed.</summary>
    Completed,

    /// <summary>The final artwork state was already durable; the journal was completed.</summary>
    CommittedFinalState,

    /// <summary>The active image observably changed; ownership was recorded and the operation aborted.</summary>
    OwnershipLost,

    /// <summary>Ownership could not be observed or compared; the image was left untouched.</summary>
    OwnershipUnknown,

    /// <summary>The Jellyfin item is absent; a removal tombstone was written with no image mutation.</summary>
    ItemRemoved,

    /// <summary>The operation, a required artifact, or an observation is invalid or ambiguous; no automatic mutation is permitted.</summary>
    RecoveryBlocked,

    /// <summary>The operation requires a lifecycle-owned mutation that is not implemented yet; it was left in place.</summary>
    Deferred,

    /// <summary>The reconciliation was cancelled; the active artwork was left unchanged.</summary>
    Cancelled,
}
