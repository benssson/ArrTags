using System;

namespace ArrTags.Artwork;

/// <summary>
/// The durable write-ahead operation record for one publication or restoration
/// attempt, following <c>docs/data-model/03-10-artworkcacheentry.md</c> section 3.10.3 and ADR-003. It
/// bridges the non-transactional Jellyfin image APIs and the plugin-owned
/// <see cref="PublishedArtworkState"/>: the complete intent, the exact before
/// identity, the candidate after content, and the artifact references are
/// persisted before the first external image mutation so an interruption can be
/// reconciled by postcondition. Only one non-terminal operation may exist per
/// item/image surface, and the record never contains a credential, media path,
/// raw external payload, or the artifact bytes themselves.
/// </summary>
/// <remarks>
/// The phase is a lower-bound marker. Recovery must assume the external call may
/// have happened whenever <see cref="Phase"/> is
/// <see cref="ArtworkOperationPhase.MutationStarted"/> or later. The
/// <see cref="SourcePresence"/> and <see cref="CandidateAfterPresence"/> fields
/// make the documented conditional requirements on
/// <see cref="SourceArtifactId"/> and <see cref="CandidateAfterContentSha256"/>
/// explicit; an absent after target is recorded as
/// <see cref="ArtworkImagePresence.Absent"/> rather than by omission alone.
/// </remarks>
public sealed class ArtworkOperation
{
    /// <summary>
    /// The current operation model version. Changes to the protocol semantics
    /// invalidate or migrate a record.
    /// </summary>
    public const int CurrentModelVersion = 1;

    /// <summary>
    /// The maximum bounded recovery/retry attempt counter.
    /// </summary>
    public const int MaxAttempt = 1000;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkOperation"/> class.
    /// </summary>
    /// <param name="operationId">The opaque random operation identifier.</param>
    /// <param name="kind">The operation kind.</param>
    /// <param name="jellyfinItemId">The Jellyfin item subject.</param>
    /// <param name="imageSurface">The image surface scope.</param>
    /// <param name="generation">The monotonic per-item/surface generation.</param>
    /// <param name="expectedBeforeIdentity">The exact precondition observed before the external mutation.</param>
    /// <param name="sourcePresence">The retained source baseline presence.</param>
    /// <param name="candidateAfterPresence">The candidate after-target presence.</param>
    /// <param name="phase">The durable phase.</param>
    /// <param name="lifecycleFence">The lifecycle fence in effect.</param>
    /// <param name="createdAt">The journal creation time.</param>
    /// <param name="updatedAt">The last journal update time.</param>
    /// <param name="modelVersion">The model version; defaults to the current version.</param>
    /// <param name="ownershipToken">The stable ownership-session token.</param>
    /// <param name="priorPublicationToken">The expected prior ArrTags publication when replacing an owned image.</param>
    /// <param name="publicationToken">The candidate publication identity for a publication.</param>
    /// <param name="candidateAfterContentSha256">The candidate after-target content hash when the after target is present.</param>
    /// <param name="observedAfterIdentity">The effective after identity, recorded only after readback.</param>
    /// <param name="sourceArtifactId">The retained source artifact identifier when the source is present.</param>
    /// <param name="derivedArtifactId">The durable derived artifact identifier for a publication.</param>
    /// <param name="attempt">The bounded recovery/retry attempt counter.</param>
    /// <param name="lastError">An optional redacted, bounded diagnostic summary.</param>
    /// <param name="candidatePublicationFingerprint">The logical publication fingerprint to commit when a publication postcondition is recovered.</param>
    /// <param name="rendererVersion">The renderer version that produced the candidate derived artifact.</param>
    /// <exception cref="ArgumentNullException">The surface or before identity is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An enum value is undefined.</exception>
    public ArtworkOperation(
        string operationId,
        ArtworkOperationKind kind,
        Guid jellyfinItemId,
        ArtworkImageSurface imageSurface,
        long generation,
        ActiveImageIdentity expectedBeforeIdentity,
        ArtworkImagePresence sourcePresence,
        ArtworkImagePresence candidateAfterPresence,
        ArtworkOperationPhase phase,
        ArtworkLifecycleFence lifecycleFence,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        int modelVersion = CurrentModelVersion,
        string? ownershipToken = null,
        string? priorPublicationToken = null,
        string? publicationToken = null,
        string? candidateAfterContentSha256 = null,
        ActiveImageIdentity? observedAfterIdentity = null,
        string? sourceArtifactId = null,
        string? derivedArtifactId = null,
        int attempt = 0,
        string? lastError = null,
        string? candidatePublicationFingerprint = null,
        int? rendererVersion = null)
    {
        ArgumentNullException.ThrowIfNull(imageSurface);
        ArgumentNullException.ThrowIfNull(expectedBeforeIdentity);
        ThrowIfUndefined(kind, nameof(kind), "operation kind");
        ThrowIfUndefined(sourcePresence, nameof(sourcePresence), "source presence");
        ThrowIfUndefined(candidateAfterPresence, nameof(candidateAfterPresence), "after-target presence");
        ThrowIfUndefined(phase, nameof(phase), "operation phase");
        ThrowIfUndefined(lifecycleFence, nameof(lifecycleFence), "lifecycle fence");

        OperationId = operationId;
        Kind = kind;
        JellyfinItemId = jellyfinItemId;
        ImageSurface = imageSurface;
        Generation = generation;
        ExpectedBeforeIdentity = expectedBeforeIdentity;
        SourcePresence = sourcePresence;
        CandidateAfterPresence = candidateAfterPresence;
        Phase = phase;
        LifecycleFence = lifecycleFence;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        ModelVersion = modelVersion;
        OwnershipToken = ownershipToken;
        PriorPublicationToken = priorPublicationToken;
        PublicationToken = publicationToken;
        CandidateAfterContentSha256 = candidateAfterContentSha256;
        ObservedAfterIdentity = observedAfterIdentity;
        SourceArtifactId = sourceArtifactId;
        DerivedArtifactId = derivedArtifactId;
        Attempt = attempt;
        LastError = ArtworkOperationErrors.Sanitize(lastError);
        CandidatePublicationFingerprint = candidatePublicationFingerprint;
        RendererVersion = rendererVersion;
    }

    /// <summary>
    /// Gets the model version.
    /// </summary>
    public int ModelVersion { get; }

    /// <summary>
    /// Gets the opaque random operation identifier.
    /// </summary>
    public string OperationId { get; }

    /// <summary>
    /// Gets the operation kind.
    /// </summary>
    public ArtworkOperationKind Kind { get; }

    /// <summary>
    /// Gets the Jellyfin item subject.
    /// </summary>
    public Guid JellyfinItemId { get; }

    /// <summary>
    /// Gets the image surface scope.
    /// </summary>
    public ArtworkImageSurface ImageSurface { get; }

    /// <summary>
    /// Gets the monotonic per-item/surface generation.
    /// </summary>
    public long Generation { get; }

    /// <summary>
    /// Gets the exact precondition observed before the external image mutation.
    /// </summary>
    public ActiveImageIdentity ExpectedBeforeIdentity { get; }

    /// <summary>
    /// Gets the retained source baseline presence.
    /// </summary>
    public ArtworkImagePresence SourcePresence { get; }

    /// <summary>
    /// Gets the candidate after-target presence.
    /// </summary>
    public ArtworkImagePresence CandidateAfterPresence { get; }

    /// <summary>
    /// Gets the durable phase.
    /// </summary>
    public ArtworkOperationPhase Phase { get; }

    /// <summary>
    /// Gets the lifecycle fence in effect.
    /// </summary>
    public ArtworkLifecycleFence LifecycleFence { get; }

    /// <summary>
    /// Gets the journal creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the last journal update time.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Gets the stable ownership-session token.
    /// </summary>
    public string? OwnershipToken { get; }

    /// <summary>
    /// Gets the expected prior ArrTags publication when replacing an owned image.
    /// </summary>
    public string? PriorPublicationToken { get; }

    /// <summary>
    /// Gets the candidate publication identity for a publication.
    /// </summary>
    public string? PublicationToken { get; }

    /// <summary>
    /// Gets the candidate after-target content hash when the after target is present.
    /// </summary>
    public string? CandidateAfterContentSha256 { get; }

    /// <summary>
    /// Gets the effective after identity, recorded only after readback.
    /// </summary>
    public ActiveImageIdentity? ObservedAfterIdentity { get; }

    /// <summary>
    /// Gets the retained source artifact identifier when the source is present.
    /// </summary>
    public string? SourceArtifactId { get; }

    /// <summary>
    /// Gets the durable derived artifact identifier for a publication.
    /// </summary>
    public string? DerivedArtifactId { get; }

    /// <summary>
    /// Gets the bounded recovery/retry attempt counter.
    /// </summary>
    public int Attempt { get; }

    /// <summary>
    /// Gets the redacted, bounded diagnostic summary, or <see langword="null"/>.
    /// </summary>
    public string? LastError { get; }

    /// <summary>
    /// Gets the logical publication fingerprint to commit when a publication
    /// postcondition is recovered, or <see langword="null"/> when the operation
    /// did not record one. A publication created by the publisher always records
    /// one; recovery refuses to reconstruct a final state without it.
    /// </summary>
    public string? CandidatePublicationFingerprint { get; }

    /// <summary>
    /// Gets the renderer version that produced the candidate derived artifact, or
    /// <see langword="null"/> when the operation did not record one.
    /// </summary>
    public int? RendererVersion { get; }

    /// <summary>
    /// Gets a value indicating whether the operation reached a terminal phase and
    /// is eligible for bounded terminal-provenance retention cleanup.
    /// </summary>
    public bool IsTerminal => ArtworkOperationPhases.IsTerminal(Phase);

    /// <summary>
    /// Gets a value indicating whether an external image mutation may already
    /// have happened and must be assumed during recovery.
    /// </summary>
    public bool MayHaveStartedMutation => ArtworkOperationPhases.MayHaveStartedMutation(Phase);

    /// <summary>
    /// Validates the documented cross-field invariants. Construction already
    /// enforces the null and enum contracts; this method enforces the semantic
    /// rules that make the record safe to persist and replay.
    /// </summary>
    /// <param name="reason">A bounded, non-secret failure explanation.</param>
    /// <returns><see langword="true"/> when the record is valid.</returns>
    public bool Validate(out string reason)
    {
        if (ModelVersion != CurrentModelVersion)
        {
            reason = "The artwork operation model version is not supported.";
            return false;
        }

        if (!ArtworkTokens.IsValid(OperationId))
        {
            reason = "An artwork operation requires a well-formed opaque operation identifier.";
            return false;
        }

        if (JellyfinItemId == Guid.Empty)
        {
            reason = "An artwork operation requires a non-empty Jellyfin item identifier.";
            return false;
        }

        if (ImageSurface.Index is not null)
        {
            reason = "Indexed image surfaces are out of V1 scope.";
            return false;
        }

        if (Generation < 0)
        {
            reason = "An artwork operation generation cannot be negative.";
            return false;
        }

        if (Attempt is < 0 or > MaxAttempt)
        {
            reason = "An artwork operation attempt counter is out of range.";
            return false;
        }

        if (CreatedAt == default(DateTimeOffset) || UpdatedAt == default(DateTimeOffset))
        {
            reason = "An artwork operation requires journal timestamps.";
            return false;
        }

        if (!ArtworkTokens.IsValid(OwnershipToken))
        {
            reason = "An artwork operation requires a well-formed opaque ownership token.";
            return false;
        }

        if (PriorPublicationToken is not null && !ArtworkTokens.IsValid(PriorPublicationToken))
        {
            reason = "The prior publication token is not a well-formed opaque token.";
            return false;
        }

        if (CandidatePublicationFingerprint is not null && !ArtworkHashes.IsSha256Hex(CandidatePublicationFingerprint))
        {
            reason = "The candidate publication fingerprint is not a SHA-256 value.";
            return false;
        }

        if (RendererVersion is <= 0)
        {
            reason = "A recorded renderer version must be positive.";
            return false;
        }

        if (!ImageSurface.Equals(ExpectedBeforeIdentity.Surface))
        {
            reason = "The expected before identity is for a different image surface.";
            return false;
        }

        if (ObservedAfterIdentity is not null && !ImageSurface.Equals(ObservedAfterIdentity.Surface))
        {
            reason = "The observed after identity is for a different image surface.";
            return false;
        }

        if (!ValidateSource(out reason))
        {
            return false;
        }

        if (!ValidateAfterTarget(out reason))
        {
            return false;
        }

        return ValidateKind(out reason);
    }

    private static void ThrowIfUndefined<T>(T value, string parameterName, string description)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, string.Concat("Unknown ", description, "."));
        }
    }

    private bool ValidateSource(out string reason)
    {
        if (SourcePresence == ArtworkImagePresence.Present)
        {
            if (!ArtworkHashes.IsSha256Hex(SourceArtifactId))
            {
                reason = "A present source requires a content-addressed source artifact identifier.";
                return false;
            }
        }
        else if (SourceArtifactId is not null)
        {
            reason = "An absent source baseline cannot reference a source artifact identifier.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool ValidateAfterTarget(out string reason)
    {
        if (CandidateAfterPresence == ArtworkImagePresence.Present)
        {
            if (!ArtworkHashes.IsSha256Hex(CandidateAfterContentSha256))
            {
                reason = "A present after target requires a SHA-256 content hash.";
                return false;
            }
        }
        else if (CandidateAfterContentSha256 is not null)
        {
            reason = "An absent after target cannot carry a content hash.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool ValidateKind(out string reason)
    {
        switch (Kind)
        {
            case ArtworkOperationKind.Publication:
                return ValidatePublication(out reason);
            case ArtworkOperationKind.Restoration:
                return ValidateRestoration(out reason);
            default:
                reason = "The artwork operation kind is not defined.";
                return false;
        }
    }

    private bool ValidatePublication(out string reason)
    {
        if (CandidateAfterPresence != ArtworkImagePresence.Present)
        {
            reason = "A publication requires a present after target.";
            return false;
        }

        if (!ArtworkTokens.IsValid(PublicationToken))
        {
            reason = "A publication requires a well-formed opaque publication token.";
            return false;
        }

        if (DerivedArtifactId is null)
        {
            reason = "A publication requires a derived artifact identifier.";
            return false;
        }

        if (!ArtworkTokens.IsValid(DerivedArtifactId))
        {
            reason = "The derived artifact identifier is not a bounded opaque identifier.";
            return false;
        }

        if (!ArtworkHashes.IsSha256Hex(CandidatePublicationFingerprint))
        {
            reason = "A publication requires the logical publication fingerprint to commit on recovery.";
            return false;
        }

        if (RendererVersion is null or <= 0)
        {
            reason = "A publication requires the positive renderer version that produced its derived artifact.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool ValidateRestoration(out string reason)
    {
        if (PublicationToken is not null)
        {
            reason = "A restoration cannot carry a candidate publication token.";
            return false;
        }

        if (DerivedArtifactId is not null)
        {
            reason = "A restoration cannot reference a derived artifact.";
            return false;
        }

        if (CandidatePublicationFingerprint is not null)
        {
            reason = "A restoration cannot carry a candidate publication fingerprint.";
            return false;
        }

        if (RendererVersion is not null)
        {
            reason = "A restoration cannot carry a renderer version.";
            return false;
        }

        if (CandidateAfterPresence != SourcePresence)
        {
            reason = "A restoration must target the same presence as its source baseline.";
            return false;
        }

        if (SourcePresence == ArtworkImagePresence.Present
            && !string.Equals(CandidateAfterContentSha256, SourceArtifactId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "A restoration of a present source must target the retained source content.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
