using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral per-subject recovery gate. Before new work is accepted
/// for a Jellyfin item/image surface it loads the durable operation record and,
/// when a non-terminal operation exists, reconciles it through the
/// <see cref="ArtworkReconciler"/> under the current lifecycle fence. New work
/// proceeds only when the durable record is absent, already terminal, or has
/// reached a terminal outcome. A corrupt record, a recovery that does not reach
/// a terminal outcome, or an older durable generation fails closed.
/// </summary>
/// <remarks>
/// The gate reuses the reconciler's data-model 3.10.4 decision table and the
/// shared per-subject gate; it never reimplements the recovery decision logic,
/// never captures the current image as a new source, never deletes an artifact,
/// and never mutates a changed or unverifiable image. It serializes with normal
/// publication through the same <see cref="ArtworkSubjectGate"/> held by the
/// reconciler and the publisher. No event or library-scan wiring is added here;
/// <see cref="IArtworkRecoveryGate"/> is driven by the Phase 6 work pipeline and
/// the startup recovery service.
/// </remarks>
public sealed class ArtworkRecoveryGate : IArtworkRecoveryGate
{
    private readonly ArtworkOperationStore _operations;
    private readonly ArtworkReconciler _reconciler;
    private readonly ArtworkLifecycleFenceStore? _fences;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkRecoveryGate"/> class.
    /// </summary>
    /// <param name="operations">The authoritative durable operation store.</param>
    /// <param name="reconciler">The provider-neutral reconciliation boundary.</param>
    /// <param name="fences">The durable active lifecycle fence store, or <see langword="null"/> to assume a normal fence.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkRecoveryGate(
        ArtworkOperationStore operations,
        ArtworkReconciler reconciler,
        ArtworkLifecycleFenceStore? fences = null)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _fences = fences;
    }

    /// <inheritdoc />
    public async Task<ArtworkRecoveryGateResult> EnsureRecoveredAsync(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (jellyfinItemId == Guid.Empty
            || surface.Index is not null
            || surface.ImageType != ArtworkImageType.Primary)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.NoOperation,
                "Only the unindexed Primary image surface can carry a durable artwork operation.");
        }

        var read = _operations.Read(jellyfinItemId, surface);
        if (read.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.Blocked,
                "The durable artwork operation record failed integrity validation; no new work is accepted.");
        }

        var operation = read.Value;
        if (operation is null)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.NoOperation,
                "There is no durable artwork operation for this subject.");
        }

        if (operation.IsTerminal)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.AlreadyTerminal,
                "The durable artwork operation already reached a terminal outcome.",
                operation.Generation,
                operation.OperationId);
        }

        ArtworkReconciliationResult reconciliation;
        try
        {
            reconciliation = await _reconciler
                .ReconcileAsync(jellyfinItemId, surface, ResolveActiveFence(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.Cancelled,
                "The artwork recovery was cancelled before new work could proceed.",
                operation.Generation,
                operation.OperationId);
        }

        if (reconciliation.Outcome == ArtworkReconciliationOutcome.Cancelled)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.Cancelled,
                reconciliation.Reason,
                operation.Generation,
                operation.OperationId,
                reconciliation);
        }

        // Postcondition: re-read the durable record after reconciliation. It must
        // be terminal and must not be an older generation than the record that
        // was reconciled; the store's monotonic generation fence is the authority
        // that a stale queued operation can never overwrite a newer record.
        var after = _operations.Read(jellyfinItemId, surface);
        if (after.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.Blocked,
                "The durable artwork operation record failed integrity validation after recovery; no new work is accepted.",
                operation.Generation,
                operation.OperationId,
                reconciliation);
        }

        var resolved = after.Value;
        if (resolved is null)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.Blocked,
                "The durable artwork operation disappeared during recovery; no new work is accepted.",
                operation.Generation,
                operation.OperationId,
                reconciliation);
        }

        if (ArtworkOperationFencing.IsStale(operation.Generation, resolved.Generation))
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.Blocked,
                "The durable artwork operation was superseded by an older generation; no new work is accepted.",
                operation.Generation,
                operation.OperationId,
                reconciliation);
        }

        if (!resolved.IsTerminal)
        {
            return ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.Deferred,
                "The artwork recovery did not reach a terminal outcome; new work is deferred.",
                resolved.Generation,
                resolved.OperationId,
                reconciliation);
        }

        return ArtworkRecoveryGateResult.Create(
            ArtworkRecoveryGateOutcome.Recovered,
            "The non-terminal artwork operation reached a terminal outcome; new work may proceed.",
            resolved.Generation,
            resolved.OperationId,
            reconciliation);
    }

    /// <inheritdoc />
    public async Task<ArtworkRecoveryScanResult> RecoverStartupAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var bounded = batchSize < 1 ? 1 : batchSize;

        IReadOnlyList<ArtworkOperation> operations;
        try
        {
            operations = _operations.Enumerate(bounded);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A bounded startup scan must never prevent the plugin from loading;
            // the per-subject gate still recovers each subject before work.
            return new ArtworkRecoveryScanResult(
                examined: 0,
                recovered: 0,
                blocked: 0,
                deferred: 0,
                alreadyTerminal: 0,
                reachedBatchLimit: false,
                cancelled: cancellationToken.IsCancellationRequested);
        }

        var examined = 0;
        var recovered = 0;
        var blocked = 0;
        var deferred = 0;
        var alreadyTerminal = 0;
        var cancelled = false;

        foreach (var operation in operations)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            examined++;
            if (operation.IsTerminal)
            {
                alreadyTerminal++;
                continue;
            }

            var result = await EnsureRecoveredAsync(operation.JellyfinItemId, operation.ImageSurface, cancellationToken)
                .ConfigureAwait(false);
            switch (result.Outcome)
            {
                case ArtworkRecoveryGateOutcome.Recovered:
                    recovered++;
                    break;
                case ArtworkRecoveryGateOutcome.Blocked:
                    blocked++;
                    break;
                case ArtworkRecoveryGateOutcome.Deferred:
                    deferred++;
                    break;
                case ArtworkRecoveryGateOutcome.Cancelled:
                    cancelled = true;
                    break;
                default:
                    // A subject whose operation became terminal concurrently is
                    // already resolved for this scan.
                    alreadyTerminal++;
                    break;
            }

            if (cancelled)
            {
                break;
            }
        }

        return new ArtworkRecoveryScanResult(
            examined,
            recovered,
            blocked,
            deferred,
            alreadyTerminal,
            reachedBatchLimit: operations.Count >= bounded,
            cancelled);
    }

    /// <summary>
    /// Resolves the fence the reconciler must treat as authoritative. An absent
    /// store means the caller supplied no durable fence; an invalid record fails
    /// closed toward the most restrictive fence rather than a normal publication
    /// window.
    /// </summary>
    private ArtworkLifecycleFence ResolveActiveFence()
    {
        if (_fences is null)
        {
            return ArtworkLifecycleFence.Normal;
        }

        var state = _fences.Read();
        return state.IsValid ? state.Fence : ArtworkLifecycleFence.Uninstall;
    }
}
