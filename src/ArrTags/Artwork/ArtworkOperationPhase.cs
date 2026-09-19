namespace ArrTags.Artwork;

/// <summary>
/// The durable phase of an <see cref="ArtworkOperation"/>. The phase is a
/// lower-bound marker, not a transaction log: once the phase is
/// <see cref="MutationStarted"/> or later, recovery must assume the external
/// image mutation may have happened even if its acknowledgment was never
/// persisted. The phase is therefore written before each external call.
/// </summary>
public enum ArtworkOperationPhase
{
    /// <summary>Source/derived artifacts and the complete intent are durable; no external image mutation has started.</summary>
    Prepared,

    /// <summary>The operation has durably recorded that <c>SaveImage</c> or the supported removal operation may have started.</summary>
    MutationStarted,

    /// <summary>The image mutation may have completed and the durable item update may have started.</summary>
    RepositoryUpdateStarted,

    /// <summary>The item-image state must be read again before finalization.</summary>
    VerificationPending,

    /// <summary>The intended postcondition was observed; the final plugin state must be durably committed.</summary>
    FinalizationPending,

    /// <summary>The final plugin state is durable and references the operation; cleanup may proceed under the artifact rules.</summary>
    Committed,

    /// <summary>The operation was safely stopped without adopting its candidate result.</summary>
    Aborted,

    /// <summary>The operation or a required observation is corrupt, unavailable, or ambiguous; no further automatic mutation.</summary>
    RecoveryBlocked,
}
