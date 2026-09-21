using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;

namespace ArrTags.Updates;

/// <summary>
/// The Phase 6 composition of restart recovery and reconciliation work. Before
/// the reconciliation pipeline processes a queued item it drives the
/// provider-neutral <see cref="IArtworkRecoveryGate"/> for the work item's
/// Jellyfin item and image surface, so a non-terminal durable artwork operation
/// is recovered through the data-model 3.10.4 decision table before any new work
/// for that subject is accepted. Metadata reconciliation itself is unchanged and
/// remains free of artwork dependencies.
/// </summary>
/// <remarks>
/// A subject whose recovery cannot reach a terminal outcome defers the work
/// (bounded retry) or fails closed (terminal) rather than processing it. The
/// gate exposes the authoritative durable generation so accepted new work can
/// only supersede it through the store's monotonic generation fence.
/// </remarks>
public sealed class ArtworkRecoveringWorkItemProcessor : IWorkItemProcessor
{
    private readonly IWorkItemProcessor _inner;
    private readonly IArtworkRecoveryGate _gate;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkRecoveringWorkItemProcessor"/> class.
    /// </summary>
    /// <param name="inner">The reconciliation pipeline that performs the new work.</param>
    /// <param name="gate">The per-subject artwork recovery gate.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkRecoveringWorkItemProcessor(IWorkItemProcessor inner, IArtworkRecoveryGate gate)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    /// <inheritdoc />
    public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
    {
        ArtworkRecoveryGateResult recovery;
        try
        {
            recovery = await _gate
                .EnsureRecoveredAsync(item.Key.ItemId, item.Key.Surface, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An unexpected recovery failure is fail-closed: the work is not
            // processed and the durable records are retained.
            return WorkProcessingResult.Terminal("The artwork recovery gate failed unexpectedly; new work was not accepted.");
        }

        return recovery.Outcome switch
        {
            ArtworkRecoveryGateOutcome.Blocked => WorkProcessingResult.Terminal(recovery.Reason),
            ArtworkRecoveryGateOutcome.Deferred => WorkProcessingResult.Transient(recovery.Reason),
            ArtworkRecoveryGateOutcome.Cancelled => WorkProcessingResult.Transient(recovery.Reason),
            _ => await _inner.ProcessAsync(item, cancellationToken).ConfigureAwait(false),
        };
    }
}
