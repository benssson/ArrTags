namespace ArrTags.Artwork;

/// <summary>
/// The bounded action selected by the data-model 3.10.4 recovery decision table
/// for one non-terminal <see cref="ArtworkOperation"/>. The action describes only
/// the reconciliation decision; it performs no image mutation and holds no
/// duplicated Jellyfin state, so the decision table can be evaluated and tested
/// independently of the publisher's deterministic execution.
/// </summary>
public enum ArtworkReconciliationAction
{
    /// <summary>The operation is already terminal; no recovery work is required.</summary>
    NothingToReconcile,

    /// <summary>The current identity still matches the before identity; the deterministic operation may resume.</summary>
    Resume,

    /// <summary>The before identity still matches but the lifecycle fence refuses resuming new mutation work.</summary>
    AbortFenced,

    /// <summary>The current identity matches the after identity; the final state may be committed.</summary>
    CompleteAfter,

    /// <summary>The final artwork state is already durable and references this operation.</summary>
    FinalStateDurable,

    /// <summary>The active image observably changed; ownership is lost and no guarded mutation is permitted.</summary>
    OwnershipLost,

    /// <summary>The active image cannot be observed or compared; ownership is unknown and the image is left untouched.</summary>
    OwnershipUnknown,

    /// <summary>The Jellyfin item is absent; an item-removal tombstone is written and no image mutation is performed.</summary>
    ItemRemoved,

    /// <summary>The operation, a required artifact, or an observation is invalid or ambiguous; no automatic mutation is permitted.</summary>
    RecoveryBlocked,
}
