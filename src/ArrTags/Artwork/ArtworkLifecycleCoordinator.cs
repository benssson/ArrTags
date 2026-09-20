using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral lifecycle coordination service. When a disable or
/// uninstall fence is raised it first records the durable fence, then reconciles
/// every existing non-terminal operation to a terminal result before creating a
/// guarded restoration for every still-owned published surface. A blocked,
/// externally changed, or uncertain restoration leaves the active image and its
/// recovery records in place and reports an incomplete lifecycle result. A
/// confirmed item removal tombstones the in-flight operation and performs no
/// Jellyfin image call. The service is bounded, fail-closed, and never deletes a
/// source artifact or journal record that may be needed for recovery.
/// </summary>
public sealed class ArtworkLifecycleCoordinator : IArtworkLifecycleCoordinator
{
    /// <summary>
    /// The reason recorded when a disable drain begins.
    /// </summary>
    public const string DisableReason = "The plugin is being disabled; new publication work is refused and restoration may proceed.";

    /// <summary>
    /// The reason recorded when an uninstall drain begins.
    /// </summary>
    public const string UninstallReason = "The plugin is being uninstalled; new publication work is refused and restoration may proceed.";

    private readonly IArtworkSourceReader _reader;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly ArtworkReconciler _reconciler;
    private readonly ArtworkPublisher _publisher;
    private readonly ArtworkLifecycleFenceStore _fences;
    private readonly IPluginLifecycleFenceProvider _pluginLifecycle;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkLifecycleCoordinator"/> class.
    /// </summary>
    /// <param name="reader">The host-neutral source adapter used to confirm item removal.</param>
    /// <param name="states">The authoritative published-artwork state store.</param>
    /// <param name="operations">The authoritative durable operation store.</param>
    /// <param name="reconciler">The reconciliation boundary used to drain operations.</param>
    /// <param name="publisher">The deterministic executor that creates and runs the guarded restoration.</param>
    /// <param name="fences">The durable active lifecycle fence store.</param>
    /// <param name="pluginLifecycle">The host plugin-state fence provider.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkLifecycleCoordinator(
        IArtworkSourceReader reader,
        PublishedArtworkStateStore states,
        ArtworkOperationStore operations,
        ArtworkReconciler reconciler,
        ArtworkPublisher publisher,
        ArtworkLifecycleFenceStore fences,
        IPluginLifecycleFenceProvider pluginLifecycle)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _fences = fences ?? throw new ArgumentNullException(nameof(fences));
        _pluginLifecycle = pluginLifecycle ?? throw new ArgumentNullException(nameof(pluginLifecycle));
    }

    /// <inheritdoc />
    public void ResetStaleFence()
    {
        try
        {
            _fences.Reset();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A best-effort startup cleanup must not prevent the plugin loading.
        }
    }

    /// <inheritdoc />
    public Task<ArtworkLifecycleResult> DrainForHostShutdownAsync(CancellationToken cancellationToken)
    {
        var fence = ResolveFence();
        if (fence == ArtworkLifecycleFence.Normal)
        {
            return Task.FromResult(ArtworkLifecycleResult.Create(
                ArtworkLifecycleFence.Normal,
                ArtworkLifecycleOutcome.NothingToDo,
                "The host shutdown implies no disable or uninstall; no artwork was changed."));
        }

        return DrainAsync(fence, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ArtworkLifecycleResult> DrainAsync(
        ArtworkLifecycleFence fence,
        CancellationToken cancellationToken)
    {
        if (fence is not (ArtworkLifecycleFence.Disable or ArtworkLifecycleFence.Uninstall))
        {
            return ArtworkLifecycleResult.Create(
                fence,
                ArtworkLifecycleOutcome.NothingToDo,
                "Only a disable or uninstall fence is drained.");
        }

        try
        {
            return await DrainCoreAsync(fence, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkLifecycleResult.Create(
                fence,
                ArtworkLifecycleOutcome.Cancelled,
                "The lifecycle drain was cancelled; the durable fence and recovery records are retained.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkLifecycleResult.Create(
                fence,
                ArtworkLifecycleOutcome.Incomplete,
                "The lifecycle drain could not complete safely; the durable fence and recovery records are retained.");
        }
    }

    private async Task<ArtworkLifecycleResult> DrainCoreAsync(
        ArtworkLifecycleFence fence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The fence is durable before any drain work so new publication is
        // refused immediately and the intent survives an interruption.
        _fences.Set(fence, fence == ArtworkLifecycleFence.Uninstall ? UninstallReason : DisableReason);

        var reconciled = 0;
        var blockedOperations = 0;
        foreach (var operation in _operations.Enumerate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.IsTerminal)
            {
                // A recovery-blocked operation is terminal for the normal
                // protocol but is not resolved; report it as incomplete rather
                // than claiming the lifecycle transition completed.
                if (operation.Phase == ArtworkOperationPhase.RecoveryBlocked)
                {
                    blockedOperations++;
                }

                continue;
            }

            var result = await _reconciler
                .ReconcileAsync(operation.JellyfinItemId, operation.ImageSurface, fence, cancellationToken)
                .ConfigureAwait(false);
            if (result.Outcome == ArtworkReconciliationOutcome.Cancelled)
            {
                return ArtworkLifecycleResult.Create(
                    fence,
                    ArtworkLifecycleOutcome.Cancelled,
                    "The lifecycle drain was cancelled while reconciling an operation.",
                    reconciled,
                    0,
                    0);
            }

            // Classify by the durable operation phase rather than the transient
            // reconciliation outcome. OwnershipUnknown persists the operation as
            // RecoveryBlocked and OwnershipLost persists Aborted, so only a
            // durable Committed or Aborted phase is resolved; a RecoveryBlocked
            // or still-non-terminal operation must keep the drain incomplete.
            var durable = _operations.Read(operation.JellyfinItemId, operation.ImageSurface);
            if (durable.Status == StateReadStatus.Found
                && durable.Value is { } resolvedOperation
                && resolvedOperation.Phase is ArtworkOperationPhase.Committed or ArtworkOperationPhase.Aborted)
            {
                reconciled++;
            }
            else
            {
                blockedOperations++;
            }
        }

        var restored = 0;
        var blockedRestorations = 0;
        foreach (var state in _states.Enumerate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.State != ArtworkPublicationState.Published)
            {
                continue;
            }

            var result = await _publisher
                .RestoreAsync(state.JellyfinItemId, state.ImageSurface, fence, cancellationToken)
                .ConfigureAwait(false);
            if (result.Outcome == ArtworkReconciliationOutcome.Cancelled)
            {
                return ArtworkLifecycleResult.Create(
                    fence,
                    ArtworkLifecycleOutcome.Cancelled,
                    "The lifecycle drain was cancelled while restoring an owned surface.",
                    reconciled,
                    restored,
                    blockedRestorations);
            }

            if (result.Outcome == ArtworkReconciliationOutcome.Completed)
            {
                restored++;
            }
            else
            {
                blockedRestorations++;
            }
        }

        var complete = blockedOperations == 0 && blockedRestorations == 0;
        return ArtworkLifecycleResult.Create(
            fence,
            complete ? ArtworkLifecycleOutcome.Completed : ArtworkLifecycleOutcome.Incomplete,
            complete
                ? "Every operation was reconciled and every owned surface was restored."
                : "At least one operation or restoration remains unresolved; its recovery records are retained.",
            reconciled,
            restored,
            blockedRestorations);
    }

    /// <inheritdoc />
    public async Task<ArtworkRemovalResult> HandleItemRemovedAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (itemId == Guid.Empty
            || surface.Index is not null
            || surface.ImageType != ArtworkImageType.Primary)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.NotConfirmed,
                "Only a non-empty Jellyfin item and the unindexed Primary image surface can be tombstoned.");
        }

        try
        {
            return await HandleItemRemovedCoreAsync(itemId, surface, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.Cancelled,
                "The item removal was cancelled; nothing was changed.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.Blocked,
                "The item removal could not be recorded safely; nothing was changed.");
        }
    }

    private async Task<ArtworkRemovalResult> HandleItemRemovedCoreAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        // The library event is only a hint; a fresh read must confirm absence.
        ArtworkSourceReadResult read;
        try
        {
            read = await _reader.ReadAsync(itemId, surface, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.Cancelled,
                "The item-removal confirmation was cancelled.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.Blocked,
                "The item removal could not be confirmed safely.");
        }

        var absent = read.Status == ArtworkSourceReadStatus.Failed
            && read.FailureReason == ArtworkSourceReadFailureReason.ItemNotFound;
        if (!absent)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.NotConfirmed,
                "The item removal hint was not confirmed by a fresh read; nothing was changed.");
        }

        // Reuse the reconciler's item-removal decision to tombstone an in-flight
        // operation. The reconciler re-reads, writes the ItemRemoved tombstone,
        // and performs no image mutation.
        var operationRead = _operations.Read(itemId, surface);
        if (operationRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.Blocked,
                "The durable artwork operation record failed integrity validation; the removal is not confirmed.");
        }

        if (operationRead.Value is { } operation && !operation.IsTerminal)
        {
            try
            {
                await _reconciler.ReconcileAsync(itemId, surface, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ArtworkRemovalResult.Create(
                    ArtworkRemovalOutcome.Cancelled,
                    "The item-removal tombstone was cancelled.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return ArtworkRemovalResult.Create(
                    ArtworkRemovalOutcome.Blocked,
                    "The item-removal tombstone could not be recorded safely.");
            }

            // The reconciler only tombstones the operation on its ItemRemoved
            // decision, which persists an Aborted phase. An invalid or ambiguous
            // record leaves the operation non-terminal or RecoveryBlocked, so do
            // not claim a tombstone unless the operation was actually aborted.
            var tombstonedOperation = _operations.Read(itemId, surface);
            if (tombstonedOperation.Status != StateReadStatus.Found
                || tombstonedOperation.Value is not { } durableOperation
                || durableOperation.Phase != ArtworkOperationPhase.Aborted)
            {
                return ArtworkRemovalResult.Create(
                    ArtworkRemovalOutcome.Blocked,
                    "The in-flight artwork operation could not be tombstoned safely; its recovery records are retained.",
                    tombstoned: false);
            }
        }

        // A published state without an in-flight operation is still marked
        // removed so no image is later served or mutated for the missing item.
        var stateRead = _states.Read(itemId, surface);
        if (stateRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.Blocked,
                "The published artwork state failed integrity validation; the removal is not confirmed.",
                tombstoned: false);
        }

        if (stateRead.Value is { } state && state.State != ArtworkPublicationState.Removed)
        {
            _states.Write(PublishedArtworkStateTransitions.MarkRemoved(state, DateTimeOffset.UtcNow).State);
        }

        return ArtworkRemovalResult.Create(
            ArtworkRemovalOutcome.Confirmed,
            "The item absence was confirmed; the operation was tombstoned and no image mutation was performed.",
            tombstoned: true);
    }

    private ArtworkLifecycleFence ResolveFence()
    {
        var durable = _fences.Read();
        if (!durable.IsValid)
        {
            // A corrupt fence fails closed toward restoration rather than toward
            // a normal publication window; the per-subject state still guards
            // against an unsafe mutation.
            return ArtworkLifecycleFence.Uninstall;
        }

        if (durable.Fence != ArtworkLifecycleFence.Normal)
        {
            return durable.Fence;
        }

        return _pluginLifecycle.GetLifecycleFence();
    }
}
