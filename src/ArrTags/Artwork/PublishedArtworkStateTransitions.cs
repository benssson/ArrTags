using System;

namespace ArrTags.Artwork;

/// <summary>
/// The pure, testable guarded logical transitions from
/// <c>docs/data-model/03-10-artworkcacheentry.md</c> section 3.10.2. The component never calls a
/// Jellyfin image API and never mutates an artifact; it evaluates ownership and
/// returns the resulting state plus the action later Phase 5 tasks may drive.
/// Crash-consistent ordering is the separate <c>ArtworkOperation</c> journal
/// (task 5.6) and is intentionally not implemented here.
/// </summary>
/// <remarks>
/// The blocked states <see cref="ArtworkPublicationState.OwnershipLost"/>,
/// <see cref="ArtworkPublicationState.OwnershipUnknown"/>, and
/// <see cref="ArtworkPublicationState.RestoreBlocked"/> are never automatically
/// re-baselined: <see cref="CaptureSession"/> and <see cref="BeginPublication"/>
/// refuse to start or continue a session from them.
/// </remarks>
public static class PublishedArtworkStateTransitions
{
    /// <summary>
    /// Captures a new publication session from a fresh source observation. It
    /// records the exact retained source baseline (or explicit absence) and
    /// issues a new ownership token, but does not publish and performs no image
    /// mutation.
    /// </summary>
    /// <param name="current">The existing state, or <see langword="null"/> when none exists.</param>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface.</param>
    /// <param name="sourceCapture">The identity observed immediately before publication.</param>
    /// <param name="sourceArtifact">The retained source artifact information, or <see langword="null"/> for an absent baseline.</param>
    /// <param name="updatedAt">The transition time.</param>
    /// <returns>The transition result; a blocked or active session yields no new baseline.</returns>
    /// <exception cref="ArgumentException">The identifiers or source capture are inconsistent.</exception>
    public static ArtworkTransitionResult CaptureSession(
        PublishedArtworkState? current,
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        ActiveImageIdentity sourceCapture,
        SourceArtifactInfo? sourceArtifact,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sourceCapture);

        if (jellyfinItemId == Guid.Empty)
        {
            throw new ArgumentException("A capture session requires a non-empty Jellyfin item identifier.", nameof(jellyfinItemId));
        }

        if (!surface.Equals(sourceCapture.Surface))
        {
            throw new ArgumentException("The source capture identity is for a different image surface.", nameof(sourceCapture));
        }

        if (current is not null)
        {
            if (current.JellyfinItemId != jellyfinItemId)
            {
                throw new ArgumentException("The existing state belongs to a different Jellyfin item.", nameof(current));
            }

            if (!current.ImageSurface.Equals(surface))
            {
                throw new ArgumentException("The existing state belongs to a different image surface.", nameof(current));
            }

            if (current.State is not (ArtworkPublicationState.NotPublished
                or ArtworkPublicationState.Restored
                or ArtworkPublicationState.Removed))
            {
                return new ArtworkTransitionResult(
                    current,
                    ArtworkTransitionAction.None,
                    "An active or blocked ownership session cannot be automatically re-baselined.");
            }
        }

        ArtworkImagePresence presence;
        string? artifactId;
        string? fingerprint;

        if (sourceCapture.Presence == ArtworkImagePresence.Present)
        {
            if (sourceArtifact is null)
            {
                throw new ArgumentException("A present source capture requires a retained source artifact.", nameof(sourceArtifact));
            }

            if (!string.Equals(sourceArtifact.Sha256, sourceCapture.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The retained source artifact does not match the captured content hash.", nameof(sourceArtifact));
            }

            if (sourceCapture.ByteLength is { } capturedLength && capturedLength != sourceArtifact.ByteLength)
            {
                throw new ArgumentException("The retained source artifact does not match the captured byte length.", nameof(sourceArtifact));
            }

            presence = ArtworkImagePresence.Present;
            artifactId = sourceArtifact.ArtifactId;
            fingerprint = sourceArtifact.Sha256;
        }
        else
        {
            if (sourceArtifact is not null)
            {
                throw new ArgumentException("An absent source baseline cannot reference a source artifact.", nameof(sourceArtifact));
            }

            presence = ArtworkImagePresence.Absent;
            artifactId = null;
            fingerprint = null;
        }

        var next = new PublishedArtworkState(
            jellyfinItemId,
            surface,
            ArtworkPublicationState.NotPublished,
            updatedAt,
            stateRevision: (current?.StateRevision ?? 0) + 1,
            sourcePresence: presence,
            sourceArtifactId: artifactId,
            sourceFingerprint: fingerprint,
            sourceCaptureIdentity: sourceCapture,
            ownershipToken: ArtworkTokens.Create());

        return new ArtworkTransitionResult(
            next,
            ArtworkTransitionAction.None,
            "A new publication session was captured; publication may proceed only after a fresh baseline check.");
    }

    /// <summary>
    /// Evaluates whether the current state permits a publication. A matching
    /// identity permits <see cref="ArtworkTransitionAction.PublishDerived"/>; a
    /// changed or unobservable identity records the blocked state and prohibits
    /// mutation. A captured not-yet-published baseline that changed requires a
    /// fresh capture instead of claiming ownership was lost.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="observed">The fresh active-image observation, or <see langword="null"/> when it could not be made.</param>
    /// <param name="observedAt">The observation time.</param>
    /// <returns>The transition result.</returns>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    public static ArtworkTransitionResult BeginPublication(
        PublishedArtworkState state,
        ActiveImageIdentity? observed,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.State == ArtworkPublicationState.NotPublished)
        {
            var comparison = ArtworkOwnershipComparer.Compare(state.SourceCaptureIdentity, observed, observedAt);
            if (comparison.Status == ArtworkOwnershipStatus.Owned)
            {
                return new ArtworkTransitionResult(
                    state,
                    ArtworkTransitionAction.PublishDerived,
                    "The captured source baseline is still current; publication may proceed.");
            }

            return new ArtworkTransitionResult(
                state,
                ArtworkTransitionAction.None,
                "The captured source baseline is no longer current; a new capture is required.");
        }

        if (state.State != ArtworkPublicationState.Published)
        {
            return new ArtworkTransitionResult(
                state,
                ArtworkTransitionAction.None,
                "The current artwork state does not permit publication.");
        }

        var publishedComparison = ArtworkOwnershipComparer.Compare(state.ActiveImageIdentity, observed, observedAt);
        switch (publishedComparison.Status)
        {
            case ArtworkOwnershipStatus.Owned:
                return new ArtworkTransitionResult(
                    state,
                    ArtworkTransitionAction.PublishDerived,
                    "The active image still matches the expected identity; a repeated publication may proceed.");
            case ArtworkOwnershipStatus.Changed:
                return new ArtworkTransitionResult(
                    WithOwnership(state, ArtworkPublicationState.OwnershipLost, publishedComparison, observedAt),
                    ArtworkTransitionAction.None,
                    "The active image changed after publication; ownership is lost and no mutation is permitted.");
            default:
                return new ArtworkTransitionResult(
                    WithOwnership(state, ArtworkPublicationState.OwnershipUnknown, publishedComparison, observedAt),
                    ArtworkTransitionAction.None,
                    "Ownership could not be observed; the active image is left unchanged.");
        }
    }

    /// <summary>
    /// Commits a verified publication. It issues a new publication token and
    /// records the verified active identity, while reusing the original source
    /// artifact and ownership token so an ArrTags output is never captured as a
    /// new original.
    /// </summary>
    /// <param name="state">The state that permitted publication.</param>
    /// <param name="verifiedActiveIdentity">The active identity verified after publication.</param>
    /// <param name="publishedFingerprint">The logical publication fingerprint.</param>
    /// <param name="rendererVersion">The renderer version that produced the image.</param>
    /// <param name="updatedAt">The commit time.</param>
    /// <returns>The transition result with the committed published state.</returns>
    /// <exception cref="ArgumentException">A required publication value is invalid.</exception>
    /// <exception cref="InvalidOperationException">The state does not permit publication.</exception>
    public static ArtworkTransitionResult CommitPublication(
        PublishedArtworkState state,
        ActiveImageIdentity verifiedActiveIdentity,
        string publishedFingerprint,
        int rendererVersion,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(verifiedActiveIdentity);

        if (state.State is not (ArtworkPublicationState.NotPublished or ArtworkPublicationState.Published))
        {
            throw new InvalidOperationException("Only a captured or published state can commit a publication.");
        }

        if (state.OwnershipToken is null || state.SourcePresence is null)
        {
            throw new InvalidOperationException("A publication requires a captured ownership session.");
        }

        if (verifiedActiveIdentity.Presence != ArtworkImagePresence.Present
            || !ArtworkHashes.IsSha256Hex(verifiedActiveIdentity.ContentSha256))
        {
            throw new ArgumentException("A verified active identity must be present with a content hash.", nameof(verifiedActiveIdentity));
        }

        if (!state.ImageSurface.Equals(verifiedActiveIdentity.Surface))
        {
            throw new ArgumentException("The verified active identity is for a different image surface.", nameof(verifiedActiveIdentity));
        }

        if (!ArtworkHashes.IsSha256Hex(publishedFingerprint))
        {
            throw new ArgumentException("A publication fingerprint must be a 64-character SHA-256 hex value.", nameof(publishedFingerprint));
        }

        if (rendererVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rendererVersion), rendererVersion, "A renderer version must be positive.");
        }

        var observation = new ArtworkOwnershipObservation(
            ArtworkOwnershipStatus.Owned,
            "The active image was verified after publication.",
            verifiedActiveIdentity,
            updatedAt);

        var next = new PublishedArtworkState(
            state.JellyfinItemId,
            state.ImageSurface,
            ArtworkPublicationState.Published,
            updatedAt,
            stateRevision: state.StateRevision + 1,
            sourcePresence: state.SourcePresence,
            sourceArtifactId: state.SourceArtifactId,
            sourceFingerprint: state.SourceFingerprint,
            sourceCaptureIdentity: state.SourceCaptureIdentity,
            ownershipToken: state.OwnershipToken,
            publicationToken: ArtworkTokens.Create(),
            activeImageIdentity: verifiedActiveIdentity,
            publishedFingerprint: publishedFingerprint,
            rendererVersion: rendererVersion,
            lastOwnershipObservation: observation,
            lastOperationId: state.LastOperationId);

        return new ArtworkTransitionResult(next, ArtworkTransitionAction.None, "Publication was committed.");
    }

    /// <summary>
    /// Refreshes the persisted ownership observation of a published state
    /// without mutating the image. A changed or unobservable observation records
    /// the blocked state.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="observed">The fresh active-image observation, or <see langword="null"/>.</param>
    /// <param name="observedAt">The observation time.</param>
    /// <returns>The transition result.</returns>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    public static ArtworkTransitionResult ObserveOwnership(
        PublishedArtworkState state,
        ActiveImageIdentity? observed,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.State != ArtworkPublicationState.Published)
        {
            return new ArtworkTransitionResult(
                state,
                ArtworkTransitionAction.None,
                "Only a published state has an active ownership observation to refresh.");
        }

        var comparison = ArtworkOwnershipComparer.Compare(state.ActiveImageIdentity, observed, observedAt);
        var nextState = comparison.Status switch
        {
            ArtworkOwnershipStatus.Owned => ArtworkPublicationState.Published,
            ArtworkOwnershipStatus.Changed => ArtworkPublicationState.OwnershipLost,
            _ => ArtworkPublicationState.OwnershipUnknown,
        };

        return new ArtworkTransitionResult(
            WithOwnership(state, nextState, comparison, observedAt),
            ArtworkTransitionAction.None,
            comparison.Reason);
    }

    /// <summary>
    /// Requests restoration of a published surface (disable or uninstall). The
    /// image is not mutated here: the caller must re-observe the active image
    /// before any restoration mutation.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="updatedAt">The transition time.</param>
    /// <returns>The transition result.</returns>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    public static ArtworkTransitionResult RequestRestore(PublishedArtworkState state, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.State != ArtworkPublicationState.Published)
        {
            return new ArtworkTransitionResult(
                state,
                ArtworkTransitionAction.None,
                "Only a published state can request restoration.");
        }

        return new ArtworkTransitionResult(
            Copy(state, ArtworkPublicationState.RestorePending, updatedAt),
            ArtworkTransitionAction.None,
            "Restoration was requested; re-observe the active image before any mutation.");
    }

    /// <summary>
    /// Authorizes a restoration only after a fresh identity match and a valid
    /// retained source. A changed or unobservable identity records the blocked
    /// state; a missing or corrupt source records <see cref="ArtworkPublicationState.RestoreBlocked"/>.
    /// </summary>
    /// <param name="state">The current state, which must be restore-pending.</param>
    /// <param name="observed">The fresh active-image observation, or <see langword="null"/>.</param>
    /// <param name="sourceArtifactIntegrityValid">Whether the retained source artifact passed integrity validation.</param>
    /// <param name="observedAt">The observation time.</param>
    /// <returns>The transition result.</returns>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    public static ArtworkTransitionResult AuthorizeRestoration(
        PublishedArtworkState state,
        ActiveImageIdentity? observed,
        bool sourceArtifactIntegrityValid,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.State != ArtworkPublicationState.RestorePending)
        {
            return new ArtworkTransitionResult(
                state,
                ArtworkTransitionAction.None,
                "Restoration can only be authorized from a restore-pending state.");
        }

        if (state.SourcePresence == ArtworkImagePresence.Present && !sourceArtifactIntegrityValid)
        {
            return new ArtworkTransitionResult(
                Copy(state, ArtworkPublicationState.RestoreBlocked, observedAt),
                ArtworkTransitionAction.None,
                "The retained source artifact is missing or corrupt; restoration is blocked.");
        }

        var comparison = ArtworkOwnershipComparer.Compare(state.ActiveImageIdentity, observed, observedAt);
        switch (comparison.Status)
        {
            case ArtworkOwnershipStatus.Owned:
                return new ArtworkTransitionResult(
                    state,
                    state.SourcePresence == ArtworkImagePresence.Present
                        ? ArtworkTransitionAction.RestoreSource
                        : ArtworkTransitionAction.RemoveActiveImage,
                    state.SourcePresence == ArtworkImagePresence.Present
                        ? "The active image still matches and the source is valid; restore the retained source."
                        : "The active image still matches and the baseline was absent; remove the ArrTags image.");
            case ArtworkOwnershipStatus.Changed:
                return new ArtworkTransitionResult(
                    WithOwnership(state, ArtworkPublicationState.OwnershipLost, comparison, observedAt),
                    ArtworkTransitionAction.None,
                    "The active image changed; restoration is not permitted.");
            default:
                return new ArtworkTransitionResult(
                    WithOwnership(state, ArtworkPublicationState.OwnershipUnknown, comparison, observedAt),
                    ArtworkTransitionAction.None,
                    "The active image could not be observed; restoration is not permitted.");
        }
    }

    /// <summary>
    /// Commits a restoration attempt. A verified restoration becomes
    /// <see cref="ArtworkPublicationState.Restored"/>; an unverifiable result
    /// becomes <see cref="ArtworkPublicationState.RestoreBlocked"/> without
    /// performing another automatic mutation.
    /// </summary>
    /// <param name="state">The restore-pending state.</param>
    /// <param name="restorationVerified">Whether the resulting surface was verified.</param>
    /// <param name="updatedAt">The transition time.</param>
    /// <returns>The transition result.</returns>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    public static ArtworkTransitionResult CommitRestoration(
        PublishedArtworkState state,
        bool restorationVerified,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.State != ArtworkPublicationState.RestorePending)
        {
            return new ArtworkTransitionResult(
                state,
                ArtworkTransitionAction.None,
                "Restoration can only be committed from a restore-pending state.");
        }

        if (!restorationVerified)
        {
            return new ArtworkTransitionResult(
                Copy(state, ArtworkPublicationState.RestoreBlocked, updatedAt),
                ArtworkTransitionAction.None,
                "The restoration result could not be verified; no further automatic mutation is permitted.");
        }

        var next = new PublishedArtworkState(
            state.JellyfinItemId,
            state.ImageSurface,
            ArtworkPublicationState.Restored,
            updatedAt,
            stateRevision: state.StateRevision + 1,
            sourcePresence: state.SourcePresence,
            sourceArtifactId: state.SourceArtifactId,
            sourceFingerprint: state.SourceFingerprint,
            sourceCaptureIdentity: state.SourceCaptureIdentity,
            ownershipToken: state.OwnershipToken,
            lastOperationId: state.LastOperationId);

        return new ArtworkTransitionResult(next, ArtworkTransitionAction.None, "The recorded source was restored.");
    }

    /// <summary>
    /// Records confirmed item removal. No image operation is performed against
    /// the missing item; plugin-owned records and artifacts are cleaned only
    /// under the retention policy.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="updatedAt">The transition time.</param>
    /// <returns>The transition result.</returns>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    public static ArtworkTransitionResult MarkRemoved(PublishedArtworkState state, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new ArtworkTransitionResult(
            Copy(state, ArtworkPublicationState.Removed, updatedAt),
            ArtworkTransitionAction.None,
            "The Jellyfin item was removed; no image mutation is permitted.");
    }

    private static PublishedArtworkState WithOwnership(
        PublishedArtworkState state,
        ArtworkPublicationState nextState,
        ArtworkOwnershipObservation observation,
        DateTimeOffset updatedAt)
    {
        return new PublishedArtworkState(
            state.JellyfinItemId,
            state.ImageSurface,
            nextState,
            updatedAt,
            stateRevision: state.StateRevision + 1,
            sourcePresence: state.SourcePresence,
            sourceArtifactId: state.SourceArtifactId,
            sourceFingerprint: state.SourceFingerprint,
            sourceCaptureIdentity: state.SourceCaptureIdentity,
            ownershipToken: state.OwnershipToken,
            publicationToken: state.PublicationToken,
            activeImageIdentity: state.ActiveImageIdentity,
            publishedFingerprint: state.PublishedFingerprint,
            rendererVersion: state.RendererVersion,
            lastOwnershipObservation: observation,
            lastOperationId: state.LastOperationId);
    }

    private static PublishedArtworkState Copy(
        PublishedArtworkState state,
        ArtworkPublicationState nextState,
        DateTimeOffset updatedAt)
    {
        return new PublishedArtworkState(
            state.JellyfinItemId,
            state.ImageSurface,
            nextState,
            updatedAt,
            stateRevision: state.StateRevision + 1,
            sourcePresence: state.SourcePresence,
            sourceArtifactId: state.SourceArtifactId,
            sourceFingerprint: state.SourceFingerprint,
            sourceCaptureIdentity: state.SourceCaptureIdentity,
            ownershipToken: state.OwnershipToken,
            publicationToken: state.PublicationToken,
            activeImageIdentity: state.ActiveImageIdentity,
            publishedFingerprint: state.PublishedFingerprint,
            rendererVersion: state.RendererVersion,
            lastOwnershipObservation: state.LastOwnershipObservation,
            lastOperationId: state.LastOperationId);
    }
}
