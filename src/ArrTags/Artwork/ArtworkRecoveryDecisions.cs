using System;

namespace ArrTags.Artwork;

/// <summary>
/// The pure, provider-neutral implementation of the data-model 3.10.4
/// <see cref="ArtworkOperation"/> recovery decision table. It evaluates the
/// durable operation, the associated <see cref="PublishedArtworkState"/>, and a
/// fresh active-image observation and returns a bounded action. It performs no
/// I/O, no artifact access, and no image mutation, so an uncertain external
/// outcome can be classified deterministically without touching the active
/// artwork.
/// </summary>
/// <remarks>
/// The evaluation is postcondition-based and fail-closed. A before identity match
/// prefers resumption only under the current generation and lifecycle fence; an
/// after identity match prefers completion; an observable match to neither
/// records ownership loss; and an unobservable identity never becomes ownership.
/// Reconciliation never captures the current image as a new source.
/// </remarks>
public static class ArtworkRecoveryDecisions
{
    /// <summary>
    /// Evaluates the recovery decision table for one operation.
    /// </summary>
    /// <param name="operation">The durable operation being reconciled.</param>
    /// <param name="state">The associated published-artwork state, or <see langword="null"/> when none exists.</param>
    /// <param name="current">The fresh active-image observation, or <see langword="null"/> when it could not be observed.</param>
    /// <param name="itemAbsent">Whether the Jellyfin item is confirmed absent.</param>
    /// <param name="observedAt">The comparison time.</param>
    /// <param name="activeFence">The lifecycle fence currently active for the subject, or <see langword="null"/> to use the fence stored on the operation.</param>
    /// <returns>The bounded recovery decision.</returns>
    /// <exception cref="ArgumentNullException">The operation is <see langword="null"/>.</exception>
    public static ArtworkRecoveryDecision Evaluate(
        ArtworkOperation operation,
        PublishedArtworkState? state,
        ActiveImageIdentity? current,
        bool itemAbsent,
        DateTimeOffset observedAt,
        ArtworkLifecycleFence? activeFence = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Phase is ArtworkOperationPhase.Committed or ArtworkOperationPhase.Aborted)
        {
            return new ArtworkRecoveryDecision(
                ArtworkReconciliationAction.NothingToReconcile,
                "The artwork operation already reached a final terminal phase.");
        }

        if (itemAbsent)
        {
            return new ArtworkRecoveryDecision(
                ArtworkReconciliationAction.ItemRemoved,
                "The Jellyfin item is absent; a removal tombstone is written and no image mutation is performed.");
        }

        if (IsFinalStateDurable(operation, state))
        {
            return new ArtworkRecoveryDecision(
                ArtworkReconciliationAction.FinalStateDurable,
                "The final artwork state is durable and references this operation.");
        }

        if (current is null)
        {
            return new ArtworkRecoveryDecision(
                ArtworkReconciliationAction.OwnershipUnknown,
                "The active image identity cannot be observed or compared; the image is left untouched.");
        }

        if (MatchesObservedAfter(operation, current, observedAt)
            || MatchesCandidateAfter(operation, current))
        {
            return new ArtworkRecoveryDecision(
                ArtworkReconciliationAction.CompleteAfter,
                "The active image matches the intended after identity; the final state may be committed.");
        }

        if (ArtworkOwnershipComparer.Compare(operation.ExpectedBeforeIdentity, current, observedAt).Status
            == ArtworkOwnershipStatus.Owned)
        {
            // The currently active fence is authoritative during a disable or
            // uninstall drain; otherwise the fence stored on the operation is
            // used so a previously fenced operation stays fenced.
            var fence = activeFence ?? operation.LifecycleFence;
            if (operation.Kind == ArtworkOperationKind.Publication
                && !ArtworkOperationFencing.AllowsNewPublication(fence))
            {
                return new ArtworkRecoveryDecision(
                    ArtworkReconciliationAction.AbortFenced,
                    "The lifecycle fence refuses resuming new publication work; the operation is aborted without mutation.");
            }

            return new ArtworkRecoveryDecision(
                ArtworkReconciliationAction.Resume,
                "The active image still matches the expected before identity; the deterministic operation may resume.");
        }

        return new ArtworkRecoveryDecision(
            ArtworkReconciliationAction.OwnershipLost,
            "The active image matches neither the before nor the after identity; ownership is lost.");
    }

    /// <summary>
    /// Determines whether the durable final state already references the
    /// operation, which makes the state and its verified active identity
    /// authoritative and the journal safe to complete.
    /// </summary>
    /// <param name="operation">The durable operation.</param>
    /// <param name="state">The associated state, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the final state is durable for the operation.</returns>
    public static bool IsFinalStateDurable(ArtworkOperation operation, PublishedArtworkState? state)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (state is null
            || !string.Equals(state.LastOperationId, operation.OperationId, StringComparison.Ordinal))
        {
            return false;
        }

        return operation.Kind switch
        {
            ArtworkOperationKind.Publication => state.State == ArtworkPublicationState.Published,
            ArtworkOperationKind.Restoration => state.State == ArtworkPublicationState.Restored,
            _ => false,
        };
    }

    private static bool MatchesObservedAfter(
        ArtworkOperation operation,
        ActiveImageIdentity current,
        DateTimeOffset observedAt)
    {
        return operation.ObservedAfterIdentity is not null
            && ArtworkOwnershipComparer.Compare(operation.ObservedAfterIdentity, current, observedAt).Status
                == ArtworkOwnershipStatus.Owned;
    }

    private static bool MatchesCandidateAfter(ArtworkOperation operation, ActiveImageIdentity current)
    {
        if (operation.CandidateAfterPresence == ArtworkImagePresence.Absent)
        {
            return current.Presence == ArtworkImagePresence.Absent;
        }

        return current.Presence == ArtworkImagePresence.Present
            && operation.CandidateAfterContentSha256 is not null
            && string.Equals(current.ContentSha256, operation.CandidateAfterContentSha256, StringComparison.OrdinalIgnoreCase);
    }
}
