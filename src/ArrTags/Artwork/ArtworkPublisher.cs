using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Rendering;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral, single-subject publication orchestration. It drives the
/// durable write-ahead protocol from ADR-003 and `docs/architecture/09-persisted-artwork-rendering.md` section 9
/// for one Jellyfin item and one V1 image surface: it retains the exact source
/// baseline, promotes the completed render output to a durable artifact, persists
/// an <see cref="ArtworkOperation"/> in <see cref="ArtworkOperationPhase.Prepared"/>
/// before any external mutation, revalidates the before identity immediately
/// before mutation, calls the supported image-save and item-update boundary one
/// phase at a time, reads back the effective active image, and commits the final
/// <see cref="PublishedArtworkState"/> before marking the operation committed.
/// </summary>
/// <remarks>
/// The publisher never writes media-folder artwork or Jellyfin's image cache
/// directly and never uses the deleting filesystem-path overload; it only calls
/// the host-neutral <see cref="IArtworkImageWriter"/> boundary, whose single
/// Jellyfin implementation uses the supported stream API. Any failure or
/// uncertainty leaves the current artwork unchanged and the operation
/// non-committed; resolving an uncertain postcondition is the later
/// reconciliation task (5.7). No event, queue, or library-scan wiring is added
/// here: Phase 6 drives this entry point.
/// </remarks>
public sealed class ArtworkPublisher
{
    private readonly IArtworkSourceReader _reader;
    private readonly IArtworkImageWriter _writer;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly ArtworkLifecycleFenceStore? _fences;
    private readonly long _derivedArtifactLimitBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkPublisher"/> class.
    /// </summary>
    /// <param name="reader">The host-neutral source adapter used to capture and re-read the active image.</param>
    /// <param name="writer">The host-neutral supported image-mutation boundary.</param>
    /// <param name="artifacts">The authoritative retained source/derived artifact store.</param>
    /// <param name="states">The authoritative published-artwork state store.</param>
    /// <param name="operations">The authoritative durable operation store.</param>
    /// <param name="limits">The operational limits; the derived artifact byte limit is enforced before promotion.</param>
    /// <param name="fences">The durable active lifecycle fence, or <see langword="null"/> to accept only a normal fence.</param>
    /// <exception cref="ArgumentNullException">A dependency or the limits are <see langword="null"/>.</exception>
    public ArtworkPublisher(
        IArtworkSourceReader reader,
        IArtworkImageWriter writer,
        SourceArtifactStore artifacts,
        PublishedArtworkStateStore states,
        ArtworkOperationStore operations,
        OperationalLimits limits,
        ArtworkLifecycleFenceStore? fences = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        ArgumentNullException.ThrowIfNull(limits);
        _derivedArtifactLimitBytes = limits.DerivedArtifactLimitBytes;
        _fences = fences;
    }

    /// <summary>
    /// Publishes one completed render result for one Jellyfin item and image
    /// surface under the durable write-ahead protocol. The call serializes with
    /// any other publication for the same subject and never lets a failure or an
    /// uncertainty escape as an exception.
    /// </summary>
    /// <param name="request">The bounded publication request.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded publication result.</returns>
    /// <exception cref="ArgumentNullException">The request is <see langword="null"/>.</exception>
    public Task<ArtworkPublicationResult> PublishAsync(
        ArtworkPublicationRequest request,
        CancellationToken cancellationToken)
    {
        return PublishAsync(request, observedSource: null, cancellationToken);
    }

    /// <summary>
    /// Publishes one completed render result using an already-observed source
    /// read for a new-session capture. This is the plugin-internal entry point
    /// used by the artwork generation coordinator so the render and the retained
    /// provenance baseline come from the exact same bounded observation, closing
    /// the window in which the active source could change between the coordinator
    /// read and an independent publisher capture. It still performs the
    /// before-mutation revalidation, so a source that changes after the supplied
    /// observation prevents the mutation. It never weakens the public
    /// <see cref="PublishAsync(ArtworkPublicationRequest, CancellationToken)"/>
    /// guarantee that a caller cannot inject a source baseline.
    /// </summary>
    /// <param name="request">The bounded publication request.</param>
    /// <param name="observedSource">The exact present source read the coordinator
    /// rendered from, or <see langword="null"/> to capture a fresh baseline. A
    /// non-present observation is rejected as unavailable.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded publication result.</returns>
    /// <exception cref="ArgumentNullException">The request is <see langword="null"/>.</exception>
    internal async Task<ArtworkPublicationResult> PublishAsync(
        ArtworkPublicationRequest request,
        ArtworkSourceReadResult? observedSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateRequest(request, out var validationReason))
        {
            return ArtworkPublicationResult.Failure(ArtworkPublicationOutcome.InvalidRequest, validationReason);
        }

        if (observedSource is not null && observedSource.Status != ArtworkSourceReadStatus.Present)
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.SourceUnavailable,
                "The observed source baseline is not a present image.");
        }

        var gate = ArtworkSubjectGate.Acquire(request.JellyfinItemId, request.ImageSurface);

        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Cancelled,
                "The publication was cancelled before it started.");
        }

        try
        {
            return await PublishCoreAsync(request, observedSource, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Cancelled,
                "The publication was cancelled before it completed.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "The publication could not be completed safely.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ArtworkPublicationResult> PublishCoreAsync(
        ArtworkPublicationRequest request,
        ArtworkSourceReadResult? observedSource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = request.JellyfinItemId;
        var surface = request.ImageSurface;

        // The durable lifecycle fence is authoritative for new work: a disable,
        // uninstall, or confirmed item removal refuses it, and an invalid fence
        // record fails closed.
        var fenceState = _fences is null ? ArtworkLifecycleFenceState.Normal : _fences.Read();
        if (!fenceState.AllowsNewPublication)
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "The active lifecycle fence refuses new publication work.");
        }

        var stateRead = _states.Read(item, surface);
        if (stateRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "The published artwork state failed integrity validation; no mutation is permitted.");
        }

        var operationRead = _operations.Read(item, surface);
        if (operationRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "The durable artwork operation record failed integrity validation; no mutation is permitted.");
        }

        if (operationRead.Value is { } durableOperation
            && durableOperation.Phase is not (ArtworkOperationPhase.Committed or ArtworkOperationPhase.Aborted))
        {
            // A non-terminal or recovery-blocked operation must be reconciled
            // (task 5.7) before this subject accepts new work; starting a new
            // generation would abandon the uncertain external effect.
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "A non-terminal or recovery-blocked artwork operation already exists for this item and surface.");
        }

        var generation = operationRead.Value is null ? 1 : operationRead.Value.Generation + 1;
        var state = stateRead.Value;

        PublishedArtworkState session;
        ActiveImageIdentity expectedBefore;

        if (state is null || state.State is ArtworkPublicationState.Restored or ArtworkPublicationState.Removed)
        {
            var capture = await CaptureAsync(state, item, surface, observedSource, cancellationToken).ConfigureAwait(false);
            if (capture.Failure is not null)
            {
                return capture.Failure;
            }

            session = capture.Session!;
            expectedBefore = capture.ExpectedBefore!;
        }
        else if (state.State is ArtworkPublicationState.NotPublished or ArtworkPublicationState.Published)
        {
            var read = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
            read.TryCreateActiveImageIdentity(out var observed);

            var begin = PublishedArtworkStateTransitions.BeginPublication(state, observed, DateTimeOffset.UtcNow);
            if (begin.Action != ArtworkTransitionAction.PublishDerived)
            {
                if (!ReferenceEquals(begin.State, state))
                {
                    _states.Write(begin.State);
                }

                var outcome = state.State == ArtworkPublicationState.NotPublished
                    ? ArtworkPublicationOutcome.RecaptureRequired
                    : begin.State.State == ArtworkPublicationState.OwnershipUnknown
                        ? ArtworkPublicationOutcome.BeforeIdentityUnknown
                        : ArtworkPublicationOutcome.BeforeIdentityChanged;

                return ArtworkPublicationResult.Failure(outcome, begin.Reason);
            }

            session = state;
            expectedBefore = observed!;
        }
        else
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.NotEligible,
                "The current artwork state does not permit publication.");
        }

        if (session.SourcePresence is null || !ArtworkTokens.IsValid(session.OwnershipToken))
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "The artwork session is missing its source baseline or ownership token.");
        }

        var derived = PromoteDerived(request.Rendered);
        if (!derived.Succeeded)
        {
            return ArtworkPublicationResult.Failure(ArtworkPublicationOutcome.DerivedArtifactRejected, derived.Reason);
        }

        var now = DateTimeOffset.UtcNow;
        var operation = new ArtworkOperation(
            ArtworkTokens.Create(),
            ArtworkOperationKind.Publication,
            item,
            surface,
            generation,
            expectedBefore,
            session.SourcePresence.Value,
            ArtworkImagePresence.Present,
            ArtworkOperationPhase.Prepared,
            fenceState.Fence,
            now,
            now,
            ownershipToken: session.OwnershipToken,
            priorPublicationToken: session.PublicationToken,
            publicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: derived.Info!.Sha256,
            sourceArtifactId: session.SourceArtifactId,
            derivedArtifactId: derived.Info!.ArtifactId,
            candidatePublicationFingerprint: request.Rendered.OutputFingerprint,
            rendererVersion: RenderVersion.CurrentRendererVersion);

        // Step 4: the complete intent is durable before any external mutation.
        _operations.Write(operation);

        return await ExecutePublicationAsync(operation, session, request.Rendered.PngBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the deterministic publication protocol for a durable
    /// <see cref="ArtworkOperation"/> whose intent is already recorded. The
    /// normal publication path and restart reconciliation share this method so
    /// the supported image mutation, the durable phase ordering, the readback,
    /// and the final state commit are never reimplemented. It assumes the
    /// per-subject gate is held by the caller.
    /// </summary>
    /// <param name="operation">The durable operation to execute or resume.</param>
    /// <param name="session">The ownership session whose target state will be committed.</param>
    /// <param name="derivedBytes">The exact durable derived image bytes.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded publication result.</returns>
    private async Task<ArtworkPublicationResult> ExecutePublicationAsync(
        ArtworkOperation operation,
        PublishedArtworkState session,
        ReadOnlyMemory<byte> derivedBytes,
        CancellationToken cancellationToken)
    {
        var item = operation.JellyfinItemId;
        var surface = operation.ImageSurface;

        // Step 5: revalidate the before identity immediately before mutation.
        var revalidation = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
        revalidation.TryCreateActiveImageIdentity(out var revalidated);
        var beforeComparison = ArtworkOwnershipComparer.Compare(operation.ExpectedBeforeIdentity, revalidated, DateTimeOffset.UtcNow);
        if (beforeComparison.Status != ArtworkOwnershipStatus.Owned)
        {
            if (session.State == ArtworkPublicationState.Published)
            {
                _states.Write(
                    PublishedArtworkStateTransitions.ObserveOwnership(session, revalidated, DateTimeOffset.UtcNow).State);
            }

            _operations.Write(Advance(operation, ArtworkOperationPhase.Aborted, lastError: beforeComparison.Reason));

            var outcome = beforeComparison.Status == ArtworkOwnershipStatus.Unknown
                ? ArtworkPublicationOutcome.BeforeIdentityUnknown
                : ArtworkPublicationOutcome.BeforeIdentityChanged;

            return ArtworkPublicationResult.Failure(outcome, beforeComparison.Reason, operation.OperationId);
        }

        // Re-read and enforce the durable lifecycle fence immediately before the
        // first image mutation. A disable, uninstall, or confirmed item removal
        // raised after this operation was prepared must abort it before any
        // external effect, so an in-flight publication cannot cross the fence.
        if (!AllowsNewPublication())
        {
            _operations.Write(Advance(
                operation,
                ArtworkOperationPhase.Aborted,
                lastError: "The active lifecycle fence refuses the image mutation."));
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "The active lifecycle fence refuses new publication work.",
                operation.OperationId);
        }

        // Step 6: record the mutation lower bound when it is not already durable,
        // then call the supported image mutation. Re-running the same
        // deterministic mutation for the same durable bytes is idempotent and
        // never recaptures a source.
        var mutation = operation.Phase == ArtworkOperationPhase.Prepared
            ? Advance(operation, ArtworkOperationPhase.MutationStarted)
            : operation;
        if (!ReferenceEquals(mutation, operation))
        {
            _operations.Write(mutation);
        }

        ArtworkImageMutationResult saveResult;
        try
        {
            saveResult = await _writer
                .SaveImageAsync(item, surface, derivedBytes, RenderResult.PngContentType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _operations.Write(Advance(mutation, ArtworkOperationPhase.RecoveryBlocked, lastError: "The image mutation was cancelled."));
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Cancelled,
                "The publication was cancelled during the image mutation.",
                operation.OperationId);
        }

        if (!saveResult.Succeeded)
        {
            _operations.Write(Advance(mutation, ArtworkOperationPhase.RecoveryBlocked, lastError: saveResult.Reason));
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.ImageMutationFailed,
                saveResult.Reason,
                operation.OperationId);
        }

        // Step 7: record that the item update may start, then run the normal flow.
        var update = mutation.Phase == ArtworkOperationPhase.MutationStarted
            ? Advance(mutation, ArtworkOperationPhase.RepositoryUpdateStarted)
            : mutation;
        if (!ReferenceEquals(update, mutation))
        {
            _operations.Write(update);
        }

        ArtworkImageMutationResult updateResult;
        try
        {
            updateResult = await _writer
                .PersistItemUpdateAsync(item, surface, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _operations.Write(Advance(update, ArtworkOperationPhase.RecoveryBlocked, lastError: "The item update was cancelled."));
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Cancelled,
                "The publication was cancelled during the item update.",
                operation.OperationId);
        }

        if (!updateResult.Succeeded)
        {
            _operations.Write(Advance(update, ArtworkOperationPhase.RecoveryBlocked, lastError: updateResult.Reason));
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.RepositoryUpdateFailed,
                updateResult.Reason,
                operation.OperationId);
        }

        // Step 8: record verification pending, then read back the effective image.
        var verification = update.Phase == ArtworkOperationPhase.RepositoryUpdateStarted
            ? Advance(update, ArtworkOperationPhase.VerificationPending)
            : update;
        if (!ReferenceEquals(verification, update))
        {
            _operations.Write(verification);
        }

        var readback = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
        readback.TryCreateActiveImageIdentity(out var observedAfter);
        if (observedAfter is null)
        {
            _operations.Write(Advance(
                verification,
                ArtworkOperationPhase.RecoveryBlocked,
                lastError: "The effective active image could not be observed after the mutation."));
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.ReadbackUnknown,
                "The effective active image could not be observed after the mutation.",
                operation.OperationId);
        }

        if (!MatchesCandidate(observedAfter, surface, operation.CandidateAfterContentSha256!))
        {
            _operations.Write(Advance(
                verification,
                ArtworkOperationPhase.RecoveryBlocked,
                observedAfter,
                "The observed active image does not match the candidate publication."));
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.ReadbackMismatch,
                "The observed active image does not match the candidate publication.",
                operation.OperationId);
        }

        // Step 9: the postcondition is observed; commit the final state, then the journal.
        var finalizing = Advance(verification, ArtworkOperationPhase.FinalizationPending, observedAfter);
        _operations.Write(finalizing);

        // Re-read and enforce the durable lifecycle fence again before the final
        // commit. A fence raised while the mutation was in flight must prevent
        // this path from committing outside the drain: the verified postcondition
        // is recorded and the final state commit is left to recovery, which the
        // disable/uninstall drain runs before creating a guarded restoration.
        if (!AllowsNewPublication())
        {
            return ArtworkPublicationResult.Failure(
                ArtworkPublicationOutcome.Blocked,
                "The active lifecycle fence changed while the publication was in flight; the final state commit is deferred to reconciliation.",
                operation.OperationId);
        }

        var committedState = Commit(
            session,
            observedAfter,
            operation.CandidatePublicationFingerprint!,
            operation.RendererVersion!.Value,
            operation.OperationId);
        _states.Write(committedState);

        _operations.Write(Advance(finalizing, ArtworkOperationPhase.Committed, observedAfter));
        return ArtworkPublicationResult.Success(operation.OperationId, committedState);
    }

    /// <summary>
    /// Creates and executes one guarded restoration for a still-owned published
    /// surface under the supplied lifecycle fence. It uses the same durable
    /// phases, readback, and postcondition commit rules as publication: a present
    /// baseline is restored through the supported image-save API and an absent
    /// baseline removes the ArrTags image through the supported removal API. It
    /// serializes with publication and recovery for the same subject and never
    /// lets a failure or uncertainty escape as an exception.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="fence">The lifecycle fence in effect; it is recorded on the operation.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded reconciliation result for the restoration attempt.</returns>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    internal async Task<ArtworkReconciliationResult> RestoreAsync(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        ArtworkLifecycleFence fence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!ArtworkOperationFencing.AllowsNewRestoration(fence))
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.NothingToReconcile,
                "The active lifecycle fence refuses restoration work.");
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
                "The restoration was cancelled before it started.");
        }

        try
        {
            return await RestoreCoreAsync(jellyfinItemId, surface, fence, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Cancelled,
                "The restoration was cancelled before it completed.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The restoration could not be completed safely; the active image is left untouched.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ArtworkReconciliationResult> RestoreCoreAsync(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        ArtworkLifecycleFence fence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (jellyfinItemId == Guid.Empty
            || surface.Index is not null
            || surface.ImageType != ArtworkImageType.Primary)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.NothingToReconcile,
                "Only a non-empty Jellyfin item and the unindexed Primary image surface can be restored.");
        }

        var stateRead = _states.Read(jellyfinItemId, surface);
        if (stateRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The published artwork state failed integrity validation; no restoration is permitted.");
        }

        var state = stateRead.Value;
        if (state is not { State: ArtworkPublicationState.Published })
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.NothingToReconcile,
                "No published artwork state is eligible for restoration.",
                state: state);
        }

        var operationRead = _operations.Read(jellyfinItemId, surface);
        if (operationRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The durable artwork operation record failed integrity validation; no restoration is permitted.");
        }

        if (operationRead.Value is { } existing && !existing.IsTerminal)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "A non-terminal artwork operation must be reconciled before restoration.",
                existing.OperationId,
                state);
        }

        var now = DateTimeOffset.UtcNow;
        var restorePending = PublishedArtworkStateTransitions.RequestRestore(state, now).State;

        ReadOnlyMemory<byte> sourceBytes = ReadOnlyMemory<byte>.Empty;
        string? sourceContentType = null;
        if (state.SourcePresence == ArtworkImagePresence.Present)
        {
            if (state.SourceArtifactId is null)
            {
                var missingReference = WriteRestoreBlocked(restorePending, operationId: null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    "The published state has no retained source artifact identifier.",
                    state: missingReference);
            }

            var artifact = _artifacts.Read(state.SourceArtifactId);
            if (artifact.Status != SourceArtifactReadStatus.Found || artifact.Info is null)
            {
                var blocked = WriteRestoreBlocked(restorePending, operationId: null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    "The retained source artifact is missing or failed integrity validation; restoration is blocked.",
                    state: blocked);
            }

            sourceBytes = artifact.Bytes;
            sourceContentType = artifact.Info.ContentType;
        }

        _states.Write(restorePending);

        var generation = operationRead.Value is null ? 1 : operationRead.Value.Generation + 1;
        var present = state.SourcePresence == ArtworkImagePresence.Present;
        var operation = new ArtworkOperation(
            ArtworkTokens.Create(),
            ArtworkOperationKind.Restoration,
            jellyfinItemId,
            surface,
            generation,
            state.ActiveImageIdentity!,
            state.SourcePresence!.Value,
            state.SourcePresence.Value,
            ArtworkOperationPhase.Prepared,
            fence,
            now,
            now,
            ownershipToken: state.OwnershipToken,
            priorPublicationToken: state.PublicationToken,
            candidateAfterContentSha256: present ? state.SourceArtifactId : null,
            sourceArtifactId: present ? state.SourceArtifactId : null);

        // The complete restoration intent is durable before any external mutation.
        _operations.Write(operation);

        return await ExecuteRestorationAsync(operation, restorePending, sourceBytes, sourceContentType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the deterministic restoration protocol for a durable
    /// <see cref="ArtworkOperation"/> whose intent is already recorded. The
    /// normal restoration path and restart reconciliation share this method so
    /// the supported image mutation, the durable phase ordering, the readback,
    /// and the final state commit are never reimplemented. It assumes the
    /// per-subject gate is held by the caller.
    /// </summary>
    /// <param name="operation">The durable restoration operation to execute or resume.</param>
    /// <param name="restorePending">The restore-pending state whose target state will be committed.</param>
    /// <param name="sourceBytes">The exact retained source bytes, or empty for an absent baseline.</param>
    /// <param name="sourceContentType">The confined retained source content type, or <see langword="null"/> for an absent baseline.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded reconciliation result.</returns>
    private async Task<ArtworkReconciliationResult> ExecuteRestorationAsync(
        ArtworkOperation operation,
        PublishedArtworkState restorePending,
        ReadOnlyMemory<byte> sourceBytes,
        string? sourceContentType,
        CancellationToken cancellationToken)
    {
        var item = operation.JellyfinItemId;
        var surface = operation.ImageSurface;
        var now = DateTimeOffset.UtcNow;

        // Revalidate the active image immediately before mutation.
        var revalidation = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
        revalidation.TryCreateActiveImageIdentity(out var revalidated);
        var beforeComparison = ArtworkOwnershipComparer.Compare(operation.ExpectedBeforeIdentity, revalidated, now);
        if (beforeComparison.Status != ArtworkOwnershipStatus.Owned)
        {
            var authorized = PublishedArtworkStateTransitions
                .AuthorizeRestoration(restorePending, revalidated, sourceArtifactIntegrityValid: true, now)
                .State;
            _states.Write(WithOperationId(authorized, operation.OperationId));

            var phase = beforeComparison.Status == ArtworkOwnershipStatus.Unknown
                ? ArtworkOperationPhase.RecoveryBlocked
                : ArtworkOperationPhase.Aborted;
            PersistResolution(operation, phase, revalidated, beforeComparison.Reason, null);

            var outcome = beforeComparison.Status == ArtworkOwnershipStatus.Unknown
                ? ArtworkReconciliationOutcome.OwnershipUnknown
                : ArtworkReconciliationOutcome.OwnershipLost;
            return ArtworkReconciliationResult.Create(outcome, beforeComparison.Reason, operation.OperationId, authorized);
        }

        // Record the mutation lower bound, then perform the supported mutation.
        var mutation = operation.Phase == ArtworkOperationPhase.Prepared
            ? Advance(operation, ArtworkOperationPhase.MutationStarted)
            : operation;
        if (!ReferenceEquals(mutation, operation))
        {
            _operations.Write(mutation);
        }

        ArtworkImageMutationResult mutationResult;
        try
        {
            // A present baseline restores the retained source through the same
            // supported stream SaveImage API as publication; an absent baseline
            // removes the ArrTags image through the supported removal API.
            mutationResult = operation.CandidateAfterPresence == ArtworkImagePresence.Present
                ? await _writer.SaveImageAsync(item, surface, sourceBytes, sourceContentType!, cancellationToken).ConfigureAwait(false)
                : await _writer.RemoveImageAsync(item, surface, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PersistResolution(mutation, ArtworkOperationPhase.RecoveryBlocked, null, "The restoration mutation was cancelled.", null);
            WriteRestoreBlocked(restorePending, operation.OperationId);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Cancelled,
                "The restoration was cancelled during the image mutation.",
                operation.OperationId);
        }

        if (!mutationResult.Succeeded)
        {
            PersistResolution(mutation, ArtworkOperationPhase.RecoveryBlocked, null, mutationResult.Reason, null);
            var blocked = WriteRestoreBlocked(restorePending, operation.OperationId);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                mutationResult.Reason,
                operation.OperationId,
                blocked);
        }

        var update = mutation.Phase == ArtworkOperationPhase.MutationStarted
            ? Advance(mutation, ArtworkOperationPhase.RepositoryUpdateStarted)
            : mutation;
        if (!ReferenceEquals(update, mutation))
        {
            _operations.Write(update);
        }

        if (operation.CandidateAfterPresence == ArtworkImagePresence.Present)
        {
            // The removal ABI persists the normal item update itself; a
            // present-source restoration uses the same explicit update flow as
            // publication.
            ArtworkImageMutationResult updateResult;
            try
            {
                updateResult = await _writer.PersistItemUpdateAsync(item, surface, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                PersistResolution(update, ArtworkOperationPhase.RecoveryBlocked, null, "The restoration item update was cancelled.", null);
                WriteRestoreBlocked(restorePending, operation.OperationId);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.Cancelled,
                    "The restoration was cancelled during the item update.",
                    operation.OperationId);
            }

            if (!updateResult.Succeeded)
            {
                PersistResolution(update, ArtworkOperationPhase.RecoveryBlocked, null, updateResult.Reason, null);
                var blocked = WriteRestoreBlocked(restorePending, operation.OperationId);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    updateResult.Reason,
                    operation.OperationId,
                    blocked);
            }
        }

        var verification = update.Phase == ArtworkOperationPhase.RepositoryUpdateStarted
            ? Advance(update, ArtworkOperationPhase.VerificationPending)
            : update;
        if (!ReferenceEquals(verification, update))
        {
            _operations.Write(verification);
        }

        var readback = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
        readback.TryCreateActiveImageIdentity(out var observedAfter);
        if (observedAfter is null)
        {
            PersistResolution(verification, ArtworkOperationPhase.RecoveryBlocked, null, "The restored surface could not be observed.", null);
            WriteRestoreBlocked(restorePending, operation.OperationId);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The restored surface could not be observed.",
                operation.OperationId);
        }

        if (!MatchesRestorationCandidate(operation, observedAfter))
        {
            PersistResolution(verification, ArtworkOperationPhase.RecoveryBlocked, observedAfter, "The observed surface does not match the retained baseline.", null);
            var blocked = WriteRestoreBlocked(restorePending, operation.OperationId);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                "The observed surface does not match the retained baseline.",
                operation.OperationId,
                blocked);
        }

        var finalizing = Advance(verification, ArtworkOperationPhase.FinalizationPending, observedAfter);
        _operations.Write(finalizing);

        var restored = PublishedArtworkStateTransitions
            .CommitRestoration(restorePending, restorationVerified: true, DateTimeOffset.UtcNow)
            .State;
        restored = WithOperationId(restored, operation.OperationId);
        _states.Write(restored);

        _operations.Write(Advance(finalizing, ArtworkOperationPhase.Committed, observedAfter));
        return ArtworkReconciliationResult.Create(
            ArtworkReconciliationOutcome.Completed,
            "The retained baseline was restored and verified.",
            operation.OperationId,
            restored);
    }

    private async Task<ArtworkReconciliationResult> ResumeRestorationAsync(
        ArtworkOperation operation,
        PublishedArtworkState? state,
        CancellationToken cancellationToken)
    {
        if (state is not { State: ArtworkPublicationState.RestorePending } restorePending)
        {
            const string NoState = "The restoration operation has no restore-pending state to resume.";
            PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, NoState, null);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                NoState,
                operation.OperationId,
                state);
        }

        ReadOnlyMemory<byte> sourceBytes = ReadOnlyMemory<byte>.Empty;
        string? sourceContentType = null;
        if (operation.SourcePresence == ArtworkImagePresence.Present)
        {
            if (operation.SourceArtifactId is null)
            {
                const string NoArtifact = "The restoration operation is missing its retained source artifact reference.";
                PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, NoArtifact, null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    NoArtifact,
                    operation.OperationId,
                    state);
            }

            var artifact = _artifacts.Read(operation.SourceArtifactId);
            if (artifact.Status != SourceArtifactReadStatus.Found || artifact.Info is null)
            {
                const string CorruptArtifact = "The retained source artifact is missing or failed integrity validation.";
                PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, CorruptArtifact, null);
                var blocked = WriteRestoreBlocked(restorePending, operation.OperationId);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    CorruptArtifact,
                    operation.OperationId,
                    blocked);
            }

            sourceBytes = artifact.Bytes;
            sourceContentType = artifact.Info.ContentType;
        }

        var working = operation.Phase == ArtworkOperationPhase.RecoveryBlocked
            ? Reopen(operation, ArtworkOperationPhase.Prepared)
            : operation;

        return await ExecuteRestorationAsync(working, restorePending, sourceBytes, sourceContentType, cancellationToken)
            .ConfigureAwait(false);
    }

    private PublishedArtworkState WriteRestoreBlocked(PublishedArtworkState restorePending, string? operationId)
    {
        var blocked = PublishedArtworkStateTransitions
            .CommitRestoration(restorePending, restorationVerified: false, DateTimeOffset.UtcNow)
            .State;
        if (operationId is not null)
        {
            blocked = WithOperationId(blocked, operationId);
        }

        _states.Write(blocked);
        return blocked;
    }

    private static PublishedArtworkState WithOperationId(PublishedArtworkState state, string operationId)
    {
        return new PublishedArtworkState(
            state.JellyfinItemId,
            state.ImageSurface,
            state.State,
            state.UpdatedAt,
            state.ModelVersion,
            state.StateRevision,
            state.SourcePresence,
            state.SourceArtifactId,
            state.SourceFingerprint,
            state.SourceCaptureIdentity,
            state.OwnershipToken,
            state.PublicationToken,
            state.ActiveImageIdentity,
            state.PublishedFingerprint,
            state.RendererVersion,
            state.LastOwnershipObservation,
            operationId);
    }

    private static bool MatchesRestorationCandidate(ArtworkOperation operation, ActiveImageIdentity observedAfter)
    {
        if (operation.CandidateAfterPresence == ArtworkImagePresence.Absent)
        {
            return observedAfter.Presence == ArtworkImagePresence.Absent;
        }

        return observedAfter.Presence == ArtworkImagePresence.Present
            && operation.CandidateAfterContentSha256 is not null
            && string.Equals(observedAfter.ContentSha256, operation.CandidateAfterContentSha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SessionCapture> CaptureAsync(
        PublishedArtworkState? previous,
        Guid item,
        ArtworkImageSurface surface,
        ArtworkSourceReadResult? observedSource,
        CancellationToken cancellationToken)
    {
        ArtworkSourceReadResult read;
        if (observedSource is not null)
        {
            // The coordinator rendered from this exact observation, so the
            // retained provenance baseline and the derived artifact describe the
            // same source; the before-mutation revalidation still guards against
            // a later external change.
            read = observedSource;
        }
        else
        {
            read = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
        }

        if (!read.TryCreateActiveImageIdentity(out var captured) || captured is null)
        {
            return new SessionCapture(
                null,
                null,
                ArtworkPublicationResult.Failure(
                    ArtworkPublicationOutcome.SourceUnavailable,
                    "The current active image could not be read."));
        }

        SourceArtifactInfo? sourceArtifact = null;
        if (captured.Presence == ArtworkImagePresence.Present)
        {
            var promoted = PromoteSource(read);
            if (!promoted.Succeeded)
            {
                return new SessionCapture(
                    null,
                    null,
                    ArtworkPublicationResult.Failure(ArtworkPublicationOutcome.SourceRejected, promoted.Reason));
            }

            sourceArtifact = promoted.Info;
        }

        var capture = PublishedArtworkStateTransitions.CaptureSession(
            previous,
            item,
            surface,
            captured,
            sourceArtifact,
            DateTimeOffset.UtcNow);

        return new SessionCapture(capture.State, captured, null);
    }

    private SourceArtifactPromotionResult PromoteSource(ArtworkSourceReadResult read)
    {
        if (read.Status != ArtworkSourceReadStatus.Present || read.ContentType is null || read.ContentSha256 is null)
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.InvalidContent,
                "A present source read with a confined content type is required.");
        }

        return _artifacts.Promote(read.Bytes.Span, read.ContentType, read.ContentSha256);
    }

    private SourceArtifactPromotionResult PromoteDerived(RenderResult rendered)
    {
        if (!rendered.HasArtifact
            || rendered.PngBytes.IsEmpty
            || !string.Equals(rendered.ContentType, RenderResult.PngContentType, StringComparison.Ordinal)
            || !ArtworkHashes.IsSha256Hex(rendered.OutputHash)
            || !ArtworkHashes.IsSha256Hex(rendered.OutputFingerprint))
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.InvalidContent,
                "The render result does not carry a complete, identified PNG artifact.");
        }

        if (rendered.PngBytes.Length > _derivedArtifactLimitBytes)
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.TooLarge,
                "The derived image exceeds the configured derived-artifact byte limit.");
        }

        var actual = ArtworkHashes.ComputeSha256(rendered.PngBytes.Span);
        if (!string.Equals(actual, rendered.OutputHash, StringComparison.OrdinalIgnoreCase))
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.HashMismatch,
                "The declared derived image hash does not match the supplied bytes.");
        }

        return _artifacts.Promote(rendered.PngBytes.Span, RenderResult.PngContentType, actual);
    }

    private static PublishedArtworkState Commit(
        PublishedArtworkState session,
        ActiveImageIdentity verifiedActiveIdentity,
        string publishedFingerprint,
        int rendererVersion,
        string operationId)
    {
        var committed = PublishedArtworkStateTransitions
            .CommitPublication(
                session,
                verifiedActiveIdentity,
                publishedFingerprint,
                rendererVersion,
                DateTimeOffset.UtcNow)
            .State;

        if (string.Equals(committed.LastOperationId, operationId, StringComparison.Ordinal))
        {
            return committed;
        }

        return new PublishedArtworkState(
            committed.JellyfinItemId,
            committed.ImageSurface,
            committed.State,
            committed.UpdatedAt,
            committed.ModelVersion,
            committed.StateRevision,
            committed.SourcePresence,
            committed.SourceArtifactId,
            committed.SourceFingerprint,
            committed.SourceCaptureIdentity,
            committed.OwnershipToken,
            committed.PublicationToken,
            committed.ActiveImageIdentity,
            committed.PublishedFingerprint,
            committed.RendererVersion,
            committed.LastOwnershipObservation,
            operationId);
    }

    private static ArtworkOperation Advance(
        ArtworkOperation operation,
        ArtworkOperationPhase phase,
        ActiveImageIdentity? observedAfter = null,
        string? lastError = null)
    {
        return new ArtworkOperation(
            operation.OperationId,
            operation.Kind,
            operation.JellyfinItemId,
            operation.ImageSurface,
            operation.Generation,
            operation.ExpectedBeforeIdentity,
            operation.SourcePresence,
            operation.CandidateAfterPresence,
            phase,
            operation.LifecycleFence,
            operation.CreatedAt,
            DateTimeOffset.UtcNow,
            operation.ModelVersion,
            operation.OwnershipToken,
            operation.PriorPublicationToken,
            operation.PublicationToken,
            operation.CandidateAfterContentSha256,
            observedAfter ?? operation.ObservedAfterIdentity,
            operation.SourceArtifactId,
            operation.DerivedArtifactId,
            operation.Attempt,
            lastError ?? operation.LastError,
            operation.CandidatePublicationFingerprint,
            operation.RendererVersion);
    }

    /// <summary>
    /// Re-reads the durable active lifecycle fence and reports whether new
    /// publication work is still permitted. An absent fence store means the
    /// caller supplied no durable fence; an invalid fence record fails closed
    /// because <see cref="ArtworkLifecycleFenceState.AllowsNewPublication"/> is
    /// false for it. This is the fence enforcement used at the pre-mutation and
    /// final-commit checkpoints; the initial check in
    /// <see cref="PublishCoreAsync"/> remains the entry-point refusal.
    /// </summary>
    /// <returns><see langword="true"/> when the durable fence still permits new publication work.</returns>
    private bool AllowsNewPublication()
    {
        var fenceState = _fences is null ? ArtworkLifecycleFenceState.Normal : _fences.Read();
        return fenceState.AllowsNewPublication;
    }

    private static bool MatchesCandidate(
        ActiveImageIdentity observedAfter,
        ArtworkImageSurface surface,
        string candidateContentSha256)
    {
        return observedAfter.Presence == ArtworkImagePresence.Present
            && observedAfter.Surface.Equals(surface)
            && ArtworkHashes.IsSha256Hex(observedAfter.ContentSha256)
            && string.Equals(observedAfter.ContentSha256, candidateContentSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Executes the recovery decision selected for a durable operation. It
    /// performs the deterministic operation only when the decision and the
    /// generation/lifecycle fence permit it, commits the target plugin state when
    /// the after postcondition is observed, records ownership loss or uncertainty
    /// without mutating the image, tombstones a confirmed item removal, and
    /// blocks without replay or cleanup when the operation or a required artifact
    /// is invalid. It assumes the per-subject gate is held by the caller.
    /// </summary>
    /// <param name="operation">The durable operation being reconciled.</param>
    /// <param name="state">The associated published-artwork state, or <see langword="null"/>.</param>
    /// <param name="current">The fresh active-image observation, or <see langword="null"/> when unobservable.</param>
    /// <param name="decision">The evaluated recovery decision.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded reconciliation result.</returns>
    internal async Task<ArtworkReconciliationResult> ExecuteRecoveryAsync(
        ArtworkOperation operation,
        PublishedArtworkState? state,
        ActiveImageIdentity? current,
        ArtworkRecoveryDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(decision);

        switch (decision.Action)
        {
            case ArtworkReconciliationAction.NothingToReconcile:
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.NothingToReconcile,
                    decision.Reason,
                    operation.OperationId,
                    state);

            case ArtworkReconciliationAction.FinalStateDurable:
                PersistResolution(operation, ArtworkOperationPhase.Committed, operation.ObservedAfterIdentity, decision.Reason, null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.CommittedFinalState,
                    decision.Reason,
                    operation.OperationId,
                    state);

            case ArtworkReconciliationAction.ItemRemoved:
            {
                PersistResolution(operation, ArtworkOperationPhase.Aborted, null, decision.Reason, ArtworkLifecycleFence.ItemRemoved);
                var removed = state is null
                    ? null
                    : PublishedArtworkStateTransitions.MarkRemoved(state, DateTimeOffset.UtcNow).State;
                if (removed is not null)
                {
                    _states.Write(removed);
                }

                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.ItemRemoved,
                    decision.Reason,
                    operation.OperationId,
                    removed);
            }

            case ArtworkReconciliationAction.OwnershipLost:
            {
                var updated = MarkRecoveredOwnership(state, current, DateTimeOffset.UtcNow);
                PersistResolution(operation, ArtworkOperationPhase.Aborted, current, decision.Reason, null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.OwnershipLost,
                    decision.Reason,
                    operation.OperationId,
                    updated);
            }

            case ArtworkReconciliationAction.OwnershipUnknown:
            {
                var updated = MarkRecoveredOwnership(state, null, DateTimeOffset.UtcNow);
                PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, decision.Reason, null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.OwnershipUnknown,
                    decision.Reason,
                    operation.OperationId,
                    updated);
            }

            case ArtworkReconciliationAction.RecoveryBlocked:
                PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, decision.Reason, null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    decision.Reason,
                    operation.OperationId,
                    state);

            case ArtworkReconciliationAction.CompleteAfter:
                return await CompleteAfterRecoveryAsync(operation, state, current!, decision.Reason, cancellationToken)
                    .ConfigureAwait(false);

            case ArtworkReconciliationAction.Resume:
                return await ResumeRecoveryAsync(operation, state, cancellationToken).ConfigureAwait(false);

            case ArtworkReconciliationAction.AbortFenced:
                PersistResolution(operation, ArtworkOperationPhase.Aborted, null, decision.Reason, null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.Aborted,
                    decision.Reason,
                    operation.OperationId,
                    state);

            default:
                PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, "The recovery action is not defined.", null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    "The recovery action is not defined.",
                    operation.OperationId,
                    state);
        }
    }

    private async Task<ArtworkReconciliationResult> ResumeRecoveryAsync(
        ArtworkOperation operation,
        PublishedArtworkState? state,
        CancellationToken cancellationToken)
    {
        if (operation.Kind == ArtworkOperationKind.Restoration)
        {
            return await ResumeRestorationAsync(operation, state, cancellationToken).ConfigureAwait(false);
        }

        if (!TryValidateRecoveryArtifacts(operation, requireDerived: true, out var derivedBytes, out var reason))
        {
            PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, reason, null);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                reason,
                operation.OperationId,
                state);
        }

        var session = ReconstructPublicationSession(operation, state, DateTimeOffset.UtcNow);
        var working = operation.Phase == ArtworkOperationPhase.RecoveryBlocked
            ? Reopen(operation, ArtworkOperationPhase.Prepared)
            : operation;

        var result = await ExecutePublicationAsync(working, session, derivedBytes, cancellationToken)
            .ConfigureAwait(false);
        var latest = _states.Read(operation.JellyfinItemId, operation.ImageSurface).Value ?? state;

        if (result.Published)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Resumed,
                result.Reason,
                operation.OperationId,
                result.State);
        }

        var outcome = result.Outcome switch
        {
            ArtworkPublicationOutcome.BeforeIdentityChanged => ArtworkReconciliationOutcome.OwnershipLost,
            ArtworkPublicationOutcome.BeforeIdentityUnknown => ArtworkReconciliationOutcome.OwnershipUnknown,
            ArtworkPublicationOutcome.Cancelled => ArtworkReconciliationOutcome.Cancelled,
            _ => ArtworkReconciliationOutcome.RecoveryBlocked,
        };

        return ArtworkReconciliationResult.Create(outcome, result.Reason, operation.OperationId, latest);
    }

    private async Task<ArtworkReconciliationResult> CompleteAfterRecoveryAsync(
        ArtworkOperation operation,
        PublishedArtworkState? state,
        ActiveImageIdentity current,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRecoveryArtifacts(operation, requireDerived: false, out _, out var artifactReason))
        {
            PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, artifactReason, null);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                artifactReason,
                operation.OperationId,
                state);
        }

        // Step 7 (replayable): ensure the normal Jellyfin item update is persisted
        // so the observed image becomes the effective active image.
        ArtworkImageMutationResult update;
        try
        {
            update = await _writer
                .PersistItemUpdateAsync(operation.JellyfinItemId, operation.ImageSurface, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Cancelled,
                "The reconciliation was cancelled during the item update.",
                operation.OperationId,
                state);
        }

        if (!update.Succeeded)
        {
            PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, update.Reason, null);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.RecoveryBlocked,
                update.Reason,
                operation.OperationId,
                state);
        }

        if (operation.Kind == ArtworkOperationKind.Publication)
        {
            if (operation.CandidatePublicationFingerprint is null || operation.RendererVersion is null or <= 0)
            {
                const string Missing = "The publication operation does not record the inputs required to commit its final state.";
                PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, Missing, null);
                return ArtworkReconciliationResult.Create(
                    ArtworkReconciliationOutcome.RecoveryBlocked,
                    Missing,
                    operation.OperationId,
                    state);
            }

            var session = ReconstructPublicationSession(operation, state, DateTimeOffset.UtcNow);
            var committed = Commit(
                session,
                current,
                operation.CandidatePublicationFingerprint,
                operation.RendererVersion.Value,
                operation.OperationId);
            _states.Write(committed);
            PersistResolution(operation, ArtworkOperationPhase.Committed, current, reason, null);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Completed,
                reason,
                operation.OperationId,
                committed);
        }

        if (state is { State: ArtworkPublicationState.RestorePending })
        {
            var restored = PublishedArtworkStateTransitions
                .CommitRestoration(state, restorationVerified: true, DateTimeOffset.UtcNow)
                .State;
            _states.Write(restored);
            PersistResolution(operation, ArtworkOperationPhase.Committed, current, reason, null);
            return ArtworkReconciliationResult.Create(
                ArtworkReconciliationOutcome.Completed,
                reason,
                operation.OperationId,
                restored);
        }

        const string NoRestorationState = "The restoration operation has no restore-pending state to commit.";
        PersistResolution(operation, ArtworkOperationPhase.RecoveryBlocked, null, NoRestorationState, null);
        return ArtworkReconciliationResult.Create(
            ArtworkReconciliationOutcome.RecoveryBlocked,
            NoRestorationState,
            operation.OperationId,
            state);
    }

    private bool TryValidateRecoveryArtifacts(
        ArtworkOperation operation,
        bool requireDerived,
        out ReadOnlyMemory<byte> derivedBytes,
        out string reason)
    {
        derivedBytes = ReadOnlyMemory<byte>.Empty;

        if (operation.SourcePresence == ArtworkImagePresence.Present
            && (operation.SourceArtifactId is null
                || _artifacts.Read(operation.SourceArtifactId).Status != SourceArtifactReadStatus.Found))
        {
            reason = "The retained source artifact is missing or failed integrity validation.";
            return false;
        }

        if (requireDerived)
        {
            if (operation.DerivedArtifactId is null || operation.CandidateAfterContentSha256 is null)
            {
                reason = "The publication operation is missing its durable derived artifact reference.";
                return false;
            }

            var derived = _artifacts.Read(operation.DerivedArtifactId);
            if (derived.Status != SourceArtifactReadStatus.Found
                || derived.Info is null
                || !string.Equals(derived.Info.Sha256, operation.CandidateAfterContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                reason = "The durable derived artifact is missing or failed integrity validation.";
                return false;
            }

            derivedBytes = derived.Bytes;
        }

        reason = string.Empty;
        return true;
    }

    private PublishedArtworkState? MarkRecoveredOwnership(
        PublishedArtworkState? state,
        ActiveImageIdentity? observed,
        DateTimeOffset observedAt)
    {
        if (state is null)
        {
            return null;
        }

        if (state.State == ArtworkPublicationState.Published)
        {
            var next = PublishedArtworkStateTransitions.ObserveOwnership(state, observed, observedAt).State;
            _states.Write(next);
            return next;
        }

        if (state.State == ArtworkPublicationState.RestorePending)
        {
            var next = PublishedArtworkStateTransitions
                .AuthorizeRestoration(state, observed, sourceArtifactIntegrityValid: true, observedAt)
                .State;
            _states.Write(next);
            return next;
        }

        return state;
    }

    private void PersistResolution(
        ArtworkOperation operation,
        ArtworkOperationPhase target,
        ActiveImageIdentity? observedAfter,
        string? reason,
        ArtworkLifecycleFence? fence)
    {
        if (operation.Phase == target && (fence is null || fence == operation.LifecycleFence))
        {
            return;
        }

        // A RecoveryBlocked operation is terminal for the normal protocol, so a
        // resolution supersedes it with the next generation rather than
        // advancing the terminal phase in place.
        var generation = operation.Phase == ArtworkOperationPhase.RecoveryBlocked
            ? operation.Generation + 1
            : operation.Generation;
        var attempt = operation.Phase == ArtworkOperationPhase.RecoveryBlocked
            ? Math.Min(operation.Attempt + 1, ArtworkOperation.MaxAttempt)
            : operation.Attempt;

        var resolved = new ArtworkOperation(
            operation.OperationId,
            operation.Kind,
            operation.JellyfinItemId,
            operation.ImageSurface,
            generation,
            operation.ExpectedBeforeIdentity,
            operation.SourcePresence,
            operation.CandidateAfterPresence,
            target,
            fence ?? operation.LifecycleFence,
            operation.CreatedAt,
            DateTimeOffset.UtcNow,
            operation.ModelVersion,
            operation.OwnershipToken,
            operation.PriorPublicationToken,
            operation.PublicationToken,
            operation.CandidateAfterContentSha256,
            observedAfter ?? operation.ObservedAfterIdentity,
            operation.SourceArtifactId,
            operation.DerivedArtifactId,
            attempt,
            reason ?? operation.LastError,
            operation.CandidatePublicationFingerprint,
            operation.RendererVersion);

        _operations.Write(resolved);
    }

    private static ArtworkOperation Reopen(ArtworkOperation operation, ArtworkOperationPhase phase)
    {
        return new ArtworkOperation(
            operation.OperationId,
            operation.Kind,
            operation.JellyfinItemId,
            operation.ImageSurface,
            operation.Generation + 1,
            operation.ExpectedBeforeIdentity,
            operation.SourcePresence,
            operation.CandidateAfterPresence,
            phase,
            operation.LifecycleFence,
            operation.CreatedAt,
            DateTimeOffset.UtcNow,
            operation.ModelVersion,
            operation.OwnershipToken,
            operation.PriorPublicationToken,
            operation.PublicationToken,
            operation.CandidateAfterContentSha256,
            operation.ObservedAfterIdentity,
            operation.SourceArtifactId,
            operation.DerivedArtifactId,
            Math.Min(operation.Attempt + 1, ArtworkOperation.MaxAttempt),
            operation.LastError,
            operation.CandidatePublicationFingerprint,
            operation.RendererVersion);
    }

    private static PublishedArtworkState ReconstructPublicationSession(
        ArtworkOperation operation,
        PublishedArtworkState? state,
        DateTimeOffset updatedAt)
    {
        if (state is { State: ArtworkPublicationState.Published }
            && string.Equals(state.OwnershipToken, operation.OwnershipToken, StringComparison.Ordinal)
            && string.Equals(state.SourceArtifactId, operation.SourceArtifactId, StringComparison.Ordinal))
        {
            return state;
        }

        return new PublishedArtworkState(
            operation.JellyfinItemId,
            operation.ImageSurface,
            ArtworkPublicationState.NotPublished,
            updatedAt,
            stateRevision: (state?.StateRevision ?? 0) + 1,
            sourcePresence: operation.SourcePresence,
            sourceArtifactId: operation.SourceArtifactId,
            sourceFingerprint: operation.SourcePresence == ArtworkImagePresence.Present ? operation.SourceArtifactId : null,
            sourceCaptureIdentity: operation.ExpectedBeforeIdentity,
            ownershipToken: operation.OwnershipToken);
    }

    private static bool TryValidateRequest(ArtworkPublicationRequest request, out string reason)
    {
        if (request.JellyfinItemId == Guid.Empty)
        {
            reason = "A publication requires a non-empty Jellyfin item identifier.";
            return false;
        }

        if (request.ImageSurface.Index is not null || request.ImageSurface.ImageType != ArtworkImageType.Primary)
        {
            reason = "Only the unindexed Primary image surface is supported.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private sealed class SessionCapture
    {
        public SessionCapture(
            PublishedArtworkState? session,
            ActiveImageIdentity? expectedBefore,
            ArtworkPublicationResult? failure)
        {
            Session = session;
            ExpectedBefore = expectedBefore;
            Failure = failure;
        }

        public PublishedArtworkState? Session { get; }

        public ActiveImageIdentity? ExpectedBefore { get; }

        public ArtworkPublicationResult? Failure { get; }
    }
}
