using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral restart-reconciliation boundary for the durable
/// <see cref="ArtworkOperation"/> journal. Before new work is accepted for an
/// item/image surface, the reconciler loads the authoritative artwork state and
/// any durable operation, obtains a fresh item and active-image observation,
/// applies the data-model 3.10.4 decision table, and delegates execution to the
/// publisher's deterministic protocol. It serializes with normal publication
/// through the same per-subject gate and never captures the current image as a
/// new source, never mutates a changed or unverifiable image, and never deletes
/// an artifact that is not proven not to be the active image.
/// </summary>
/// <remarks>
/// An invocation that cannot safely resolve the operation is bounded and
/// fail-closed: the image is left untouched and the recovery records are
/// retained. No event, queue, library-scan, or startup wiring is added here;
/// Phase 6 drives this entry point and the lifecycle task (5.9) owns the
/// guarded restoration execution that this reconciler deliberately defers.
/// </remarks>
public sealed class ArtworkReconciler
{
    private readonly IArtworkSourceReader _reader;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly ArtworkPublisher _publisher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkReconciler"/> class.
    /// </summary>
    /// <param name="reader">The host-neutral source adapter used to re-observe the item and active image.</param>
    /// <param name="states">The authoritative published-artwork state store.</param>
    /// <param name="operations">The authoritative durable operation store.</param>
    /// <param name="publisher">The deterministic publication executor whose protocol is reused for resumption.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkReconciler(
        IArtworkSourceReader reader,
        PublishedArtworkStateStore states,
        ArtworkOperationStore operations,
        ArtworkPublisher publisher)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    /// <summary>
    /// Reconciles the durable artwork operation for one Jellyfin item and image
    /// surface. The call serializes with normal publication for the same subject
    /// and never lets a failure or an uncertainty escape as an exception.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded reconciliation result.</returns>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    public async Task<ArtworkReconciliationResult> ReconcileAsync(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        return await ReconcileAsync(jellyfinItemId, surface, ArtworkLifecycleFence.Normal, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reconciles the durable artwork operation for one Jellyfin item and image
    /// surface under an explicit active lifecycle fence. During a disable or
    /// uninstall drain the active fence is authoritative for an in-flight
    /// publication, so a prepared publication is aborted rather than resumed
    /// even when it was created before the fence was raised. The call serializes
    /// with normal publication for the same subject and never lets a failure or
    /// an uncertainty escape as an exception.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="activeFence">The lifecycle fence currently active for the subject.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded reconciliation result.</returns>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    public async Task<ArtworkReconciliationResult> ReconcileAsync(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        ArtworkLifecycleFence activeFence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (jellyfinItemId == Guid.Empty
            || surface.Index is not null
            || surface.ImageType != ArtworkImageType.Primary)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.NothingToReconcile,
                "Only a non-empty Jellyfin item and the unindexed Primary image surface can be reconciled.");
        }

        var gate = ArtworkSubjectGate.Acquire(jellyfinItemId, surface);
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Cancelled,
                "The reconciliation was cancelled before it started.");
        }

        try
        {
            return await ReconcileCoreAsync(jellyfinItemId, surface, activeFence, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Cancelled,
                "The reconciliation was cancelled before it completed.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The reconciliation could not be completed safely; the image is left untouched.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ArtworkReconciliationResult> ReconcileCoreAsync(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        ArtworkLifecycleFence activeFence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stateRead = _states.Read(jellyfinItemId, surface);
        if (stateRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The published artwork state failed integrity validation; no recovery mutation is permitted.");
        }

        var operationRead = _operations.Read(jellyfinItemId, surface);
        if (operationRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The durable artwork operation record failed integrity validation; no recovery mutation is permitted.");
        }

        var operation = operationRead.Value;
        if (operation is null)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.NothingToReconcile,
                "There is no durable artwork operation to reconcile.");
        }

        var read = await _reader.ReadAsync(jellyfinItemId, surface, cancellationToken).ConfigureAwait(false);
        var itemAbsent = read.Status == ArtworkSourceReadStatus.Failed
            && read.FailureReason == ArtworkSourceReadFailureReason.ItemNotFound;
        read.TryCreateActiveImageIdentity(out var current);

        var decision = ArtworkRecoveryDecisions.Evaluate(
            operation,
            stateRead.Value,
            current,
            itemAbsent,
            DateTimeOffset.UtcNow,
            activeFence == ArtworkLifecycleFence.Normal ? null : activeFence);

        return await _publisher
            .ExecuteRecoveryAsync(operation, stateRead.Value, current, decision, cancellationToken)
            .ConfigureAwait(false);
    }
}
