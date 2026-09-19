using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Rendering;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral, single-subject publication orchestration. It drives the
/// durable write-ahead protocol from ADR-003 and `docs/architecture.md` section 9
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubjectGates = new(StringComparer.Ordinal);

    private readonly IArtworkSourceReader _reader;
    private readonly IArtworkImageWriter _writer;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
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
    /// <exception cref="ArgumentNullException">A dependency or the limits are <see langword="null"/>.</exception>
    public ArtworkPublisher(
        IArtworkSourceReader reader,
        IArtworkImageWriter writer,
        SourceArtifactStore artifacts,
        PublishedArtworkStateStore states,
        ArtworkOperationStore operations,
        OperationalLimits limits)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        ArgumentNullException.ThrowIfNull(limits);
        _derivedArtifactLimitBytes = limits.DerivedArtifactLimitBytes;
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
    public async Task<ArtworkPublicationResult> PublishAsync(
        ArtworkPublicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidateRequest(request, out var validationReason))
        {
            return ArtworkPublicationResult.Failure(ArtworkPublicationOutcome.InvalidRequest, validationReason);
        }

        var gate = SubjectGates.GetOrAdd(
            ArtworkOperationStore.GetRecordId(request.JellyfinItemId, request.ImageSurface),
            static _ => new SemaphoreSlim(1, 1));

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
            return await PublishCoreAsync(request, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = request.JellyfinItemId;
        var surface = request.ImageSurface;

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
            var capture = await CaptureAsync(state, item, surface, cancellationToken).ConfigureAwait(false);
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
            ArtworkLifecycleFence.Normal,
            now,
            now,
            ownershipToken: session.OwnershipToken,
            priorPublicationToken: session.PublicationToken,
            publicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: derived.Info!.Sha256,
            sourceArtifactId: session.SourceArtifactId,
            derivedArtifactId: derived.Info!.ArtifactId);

        // Step 4: the complete intent is durable before any external mutation.
        _operations.Write(operation);

        // Step 5: revalidate the before identity immediately before mutation.
        var revalidation = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
        revalidation.TryCreateActiveImageIdentity(out var revalidated);
        var beforeComparison = ArtworkOwnershipComparer.Compare(expectedBefore, revalidated, DateTimeOffset.UtcNow);
        if (beforeComparison.Status != ArtworkOwnershipStatus.Owned)
        {
            if (session.State == ArtworkPublicationState.Published)
            {
                _states.Write(
                    PublishedArtworkStateTransitions.ObserveOwnership(session, revalidated, DateTimeOffset.UtcNow).State);
            }

            var aborted = Advance(operation, ArtworkOperationPhase.Aborted, lastError: beforeComparison.Reason);
            _operations.Write(aborted);

            var outcome = beforeComparison.Status == ArtworkOwnershipStatus.Unknown
                ? ArtworkPublicationOutcome.BeforeIdentityUnknown
                : ArtworkPublicationOutcome.BeforeIdentityChanged;

            return ArtworkPublicationResult.Failure(outcome, beforeComparison.Reason, operation.OperationId);
        }

        // Step 6: record that the supported image mutation may start, then call it.
        var mutation = Advance(operation, ArtworkOperationPhase.MutationStarted);
        _operations.Write(mutation);

        ArtworkImageMutationResult saveResult;
        try
        {
            saveResult = await _writer
                .SaveImageAsync(item, surface, request.Rendered.PngBytes, RenderResult.PngContentType, cancellationToken)
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
        var update = Advance(mutation, ArtworkOperationPhase.RepositoryUpdateStarted);
        _operations.Write(update);

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
        var verification = Advance(update, ArtworkOperationPhase.VerificationPending);
        _operations.Write(verification);

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

        if (!MatchesCandidate(observedAfter, surface, derived.Info!.Sha256))
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

        var committedState = Commit(session, observedAfter, request.Rendered.OutputFingerprint!, operation.OperationId);
        _states.Write(committedState);

        _operations.Write(Advance(finalizing, ArtworkOperationPhase.Committed, observedAfter));
        return ArtworkPublicationResult.Success(operation.OperationId, committedState);
    }

    private async Task<SessionCapture> CaptureAsync(
        PublishedArtworkState? previous,
        Guid item,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        var read = await _reader.ReadAsync(item, surface, cancellationToken).ConfigureAwait(false);
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
        string operationId)
    {
        var committed = PublishedArtworkStateTransitions
            .CommitPublication(
                session,
                verifiedActiveIdentity,
                publishedFingerprint,
                RenderVersion.CurrentRendererVersion,
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
            lastError ?? operation.LastError);
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
