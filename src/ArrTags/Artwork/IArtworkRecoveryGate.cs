using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral gate that recovers a non-terminal durable artwork
/// operation before new work for the same item/image surface is accepted. The
/// Phase 6 work pipeline calls it before processing a queued item so that
/// restart reconciliation always precedes normal publication, and the startup
/// scan drives it across the persisted operation records.
/// </summary>
public interface IArtworkRecoveryGate
{
    /// <summary>
    /// Ensures the durable artwork operation for one item/image surface reaches a
    /// terminal outcome before new work is accepted. When a non-terminal
    /// operation exists the call reconciles it through the provider-neutral
    /// reconciler under the current lifecycle fence and re-reads the durable
    /// record as a postcondition.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded gate result; only <c>CanProceed</c> results permit new work.</returns>
    Task<ArtworkRecoveryGateResult> EnsureRecoveredAsync(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a bounded, cancellation-aware startup recovery over the persisted
    /// durable operations. At most <paramref name="batchSize"/> records are
    /// examined; any remaining subject is recovered lazily by
    /// <see cref="EnsureRecoveredAsync"/> before new work for that subject.
    /// </summary>
    /// <param name="batchSize">The bounded maximum number of records to examine.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded scan summary.</returns>
    Task<ArtworkRecoveryScanResult> RecoverStartupAsync(
        int batchSize,
        CancellationToken cancellationToken);
}
