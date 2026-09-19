using System;

namespace ArrTags.Artwork;

/// <summary>
/// The authoritative, plugin-owned state connecting a retained source-artwork
/// baseline to a derived image currently published as Jellyfin item artwork. It
/// is the authority for publication ownership and guarded restoration; Jellyfin
/// image metadata alone is not enough. One record exists per Jellyfin item and
/// image surface. The record never contains credentials, media paths, raw
/// external payloads, or the retained source bytes themselves.
/// </summary>
public sealed class PublishedArtworkState
{
    /// <summary>
    /// The current published-artwork-state model version. Changes to ownership
    /// semantics invalidate or migrate a record.
    /// </summary>
    public const int CurrentModelVersion = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishedArtworkState"/> class.
    /// </summary>
    /// <param name="jellyfinItemId">The local subject whose active image may be derived.</param>
    /// <param name="imageSurface">The item/image surface this state controls.</param>
    /// <param name="state">The guarded ownership state.</param>
    /// <param name="updatedAt">The last state-change time.</param>
    /// <param name="modelVersion">The model version; defaults to the current version.</param>
    /// <param name="stateRevision">The monotonic committed transition revision.</param>
    /// <param name="sourcePresence">The recorded source presence, when a session exists.</param>
    /// <param name="sourceArtifactId">The content-addressed retained source artifact identifier.</param>
    /// <param name="sourceFingerprint">The SHA-256 of the retained source bytes.</param>
    /// <param name="sourceCaptureIdentity">The identity observed immediately before the first publication.</param>
    /// <param name="ownershipToken">The stable random session token.</param>
    /// <param name="publicationToken">The random token for the active derived publication.</param>
    /// <param name="activeImageIdentity">The expected identity of the active ArrTags image.</param>
    /// <param name="publishedFingerprint">The logical publication fingerprint.</param>
    /// <param name="rendererVersion">The renderer version that produced the active image.</param>
    /// <param name="lastOwnershipObservation">The latest ownership comparison.</param>
    /// <param name="lastOperationId">The durable artwork operation that produced this state, when known.</param>
    /// <exception cref="ArgumentOutOfRangeException">A required scalar is out of range.</exception>
    public PublishedArtworkState(
        Guid jellyfinItemId,
        ArtworkImageSurface imageSurface,
        ArtworkPublicationState state,
        DateTimeOffset updatedAt,
        int modelVersion = CurrentModelVersion,
        long stateRevision = 0,
        ArtworkImagePresence? sourcePresence = null,
        string? sourceArtifactId = null,
        string? sourceFingerprint = null,
        ActiveImageIdentity? sourceCaptureIdentity = null,
        string? ownershipToken = null,
        string? publicationToken = null,
        ActiveImageIdentity? activeImageIdentity = null,
        string? publishedFingerprint = null,
        int? rendererVersion = null,
        ArtworkOwnershipObservation? lastOwnershipObservation = null,
        string? lastOperationId = null)
    {
        ArgumentNullException.ThrowIfNull(imageSurface);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown publication state.");
        }

        JellyfinItemId = jellyfinItemId;
        ImageSurface = imageSurface;
        State = state;
        UpdatedAt = updatedAt;
        ModelVersion = modelVersion;
        StateRevision = stateRevision;
        SourcePresence = sourcePresence;
        SourceArtifactId = sourceArtifactId;
        SourceFingerprint = sourceFingerprint;
        SourceCaptureIdentity = sourceCaptureIdentity;
        OwnershipToken = ownershipToken;
        PublicationToken = publicationToken;
        ActiveImageIdentity = activeImageIdentity;
        PublishedFingerprint = publishedFingerprint;
        RendererVersion = rendererVersion;
        LastOwnershipObservation = lastOwnershipObservation;
        LastOperationId = lastOperationId;
    }

    /// <summary>
    /// Gets the model version.
    /// </summary>
    public int ModelVersion { get; }

    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public Guid JellyfinItemId { get; }

    /// <summary>
    /// Gets the image surface.
    /// </summary>
    public ArtworkImageSurface ImageSurface { get; }

    /// <summary>
    /// Gets the guarded ownership state.
    /// </summary>
    public ArtworkPublicationState State { get; }

    /// <summary>
    /// Gets the recorded source presence, or <see langword="null"/> when no session exists.
    /// </summary>
    public ArtworkImagePresence? SourcePresence { get; }

    /// <summary>
    /// Gets the content-addressed retained source artifact identifier, or <see langword="null"/> for an absent baseline.
    /// </summary>
    public string? SourceArtifactId { get; }

    /// <summary>
    /// Gets the SHA-256 of the retained source bytes, or <see langword="null"/> for an absent baseline.
    /// </summary>
    public string? SourceFingerprint { get; }

    /// <summary>
    /// Gets the identity observed immediately before the first ArrTags publication.
    /// </summary>
    public ActiveImageIdentity? SourceCaptureIdentity { get; }

    /// <summary>
    /// Gets the stable random ownership token for the original-to-derived session.
    /// </summary>
    public string? OwnershipToken { get; }

    /// <summary>
    /// Gets the random publication token for the active derived publication.
    /// </summary>
    public string? PublicationToken { get; }

    /// <summary>
    /// Gets the expected identity of the currently active ArrTags image.
    /// </summary>
    public ActiveImageIdentity? ActiveImageIdentity { get; }

    /// <summary>
    /// Gets the logical publication fingerprint. It is not ownership proof on its own.
    /// </summary>
    public string? PublishedFingerprint { get; }

    /// <summary>
    /// Gets the renderer version that produced the active image, or <see langword="null"/>.
    /// </summary>
    public int? RendererVersion { get; }

    /// <summary>
    /// Gets the latest ownership comparison, or <see langword="null"/> when none has been recorded.
    /// </summary>
    public ArtworkOwnershipObservation? LastOwnershipObservation { get; }

    /// <summary>
    /// Gets the durable artwork operation that produced this state, when known.
    /// </summary>
    public string? LastOperationId { get; }

    /// <summary>
    /// Gets the monotonic committed transition revision.
    /// </summary>
    public long StateRevision { get; }

    /// <summary>
    /// Gets the last state-change time.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Gets a value indicating whether the state is terminal and eligible for
    /// bounded provenance retention cleanup. Terminal never means automatically
    /// re-baselined: a blocked state remains authoritative until an explicit
    /// administrative action.
    /// </summary>
    public bool IsTerminal => State is ArtworkPublicationState.Restored
        or ArtworkPublicationState.Removed
        or ArtworkPublicationState.OwnershipLost
        or ArtworkPublicationState.OwnershipUnknown
        or ArtworkPublicationState.RestoreBlocked;

    /// <summary>
    /// Validates the documented cross-field invariants. Construction already
    /// enforces the scalar ranges; this method enforces the semantic rules that
    /// make the record safe to persist and replay.
    /// </summary>
    /// <param name="reason">A bounded, non-secret failure explanation.</param>
    /// <returns><see langword="true"/> when the record is valid.</returns>
    public bool Validate(out string reason)
    {
        if (ModelVersion != CurrentModelVersion)
        {
            reason = "The published artwork state model version is not supported.";
            return false;
        }

        if (JellyfinItemId == Guid.Empty)
        {
            reason = "A published artwork state requires a non-empty Jellyfin item identifier.";
            return false;
        }

        if (ImageSurface is null)
        {
            reason = "A published artwork state requires an image surface.";
            return false;
        }

        if (ImageSurface.Index is not null)
        {
            reason = "Indexed image surfaces are out of V1 scope.";
            return false;
        }

        if (!Enum.IsDefined(State))
        {
            reason = "The published artwork state is not a defined state.";
            return false;
        }

        if (StateRevision < 0)
        {
            reason = "The published artwork state revision cannot be negative.";
            return false;
        }

        if (UpdatedAt == default(DateTimeOffset))
        {
            reason = "A published artwork state requires an update time.";
            return false;
        }

        if (OwnershipToken is not null && !ArtworkTokens.IsValid(OwnershipToken))
        {
            reason = "The ownership token is not a well-formed opaque token.";
            return false;
        }

        if (LastOperationId is { Length: > 200 })
        {
            reason = "The last operation identifier exceeds the bounded length.";
            return false;
        }

        if (!ValidateSourceSession(out reason))
        {
            return false;
        }

        if (State is ArtworkPublicationState.Published or ArtworkPublicationState.RestorePending)
        {
            return ValidatePublished(out reason);
        }

        if (HasAnyPublicationField() && !TryValidateCompletePublicationSet(out reason))
        {
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool ValidateSourceSession(out string reason)
    {
        if (SourcePresence is null)
        {
            if (SourceArtifactId is not null || SourceFingerprint is not null || SourceCaptureIdentity is not null)
            {
                reason = "A source artifact, fingerprint, or capture identity requires a recorded source presence.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        var presence = SourcePresence.Value;
        if (!Enum.IsDefined(presence))
        {
            reason = "The recorded source presence is not defined.";
            return false;
        }

        if (!ArtworkTokens.IsValid(OwnershipToken))
        {
            reason = "A publication session requires an opaque ownership token.";
            return false;
        }

        if (SourceCaptureIdentity is null)
        {
            reason = "A publication session requires a source capture identity.";
            return false;
        }

        if (!SurfaceMatches(SourceCaptureIdentity.Surface))
        {
            reason = "The source capture identity is for a different image surface.";
            return false;
        }

        if (presence == ArtworkImagePresence.Present)
        {
            if (!ArtworkHashes.IsSha256Hex(SourceArtifactId))
            {
                reason = "A present source requires a content-addressed artifact identifier.";
                return false;
            }

            if (!ArtworkHashes.IsSha256Hex(SourceFingerprint))
            {
                reason = "A present source requires a source fingerprint.";
                return false;
            }

            if (SourceCaptureIdentity.Presence != ArtworkImagePresence.Present)
            {
                reason = "A present source requires a present source capture identity.";
                return false;
            }

            if (!string.Equals(SourceCaptureIdentity.ContentSha256, SourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                reason = "The source capture identity does not match the retained source fingerprint.";
                return false;
            }
        }
        else
        {
            if (SourceArtifactId is not null || SourceFingerprint is not null)
            {
                reason = "An absent source baseline cannot reference a source artifact or fingerprint.";
                return false;
            }

            if (SourceCaptureIdentity.Presence != ArtworkImagePresence.Absent)
            {
                reason = "An absent source baseline requires an absent source capture identity.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private bool ValidatePublished(out string reason)
    {
        if (SourcePresence is null)
        {
            reason = "A published state requires a recorded source baseline.";
            return false;
        }

        if (!ArtworkTokens.IsValid(OwnershipToken))
        {
            reason = "A published state requires an opaque ownership token.";
            return false;
        }

        if (!ArtworkTokens.IsValid(PublicationToken))
        {
            reason = "A published state requires an opaque publication token.";
            return false;
        }

        if (ActiveImageIdentity is null)
        {
            reason = "A published state requires the expected active-image identity.";
            return false;
        }

        if (ActiveImageIdentity.Presence != ArtworkImagePresence.Present)
        {
            reason = "A published state requires a present active-image identity.";
            return false;
        }

        if (!ArtworkHashes.IsSha256Hex(ActiveImageIdentity.ContentSha256))
        {
            reason = "A published state requires an active-image content hash.";
            return false;
        }

        if (!SurfaceMatches(ActiveImageIdentity.Surface))
        {
            reason = "The active-image identity is for a different image surface.";
            return false;
        }

        if (!ArtworkHashes.IsSha256Hex(PublishedFingerprint))
        {
            reason = "A published state requires a publication fingerprint.";
            return false;
        }

        if (RendererVersion is null or <= 0)
        {
            reason = "A published state requires a positive renderer version.";
            return false;
        }

        if (LastOwnershipObservation is null)
        {
            reason = "A published state requires the latest ownership observation.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TryValidateCompletePublicationSet(out string reason)
    {
        if (!ArtworkTokens.IsValid(PublicationToken)
            || ActiveImageIdentity is null
            || ActiveImageIdentity.Presence != ArtworkImagePresence.Present
            || !ArtworkHashes.IsSha256Hex(ActiveImageIdentity.ContentSha256)
            || !SurfaceMatches(ActiveImageIdentity.Surface)
            || !ArtworkHashes.IsSha256Hex(PublishedFingerprint)
            || RendererVersion is null or <= 0
            || LastOwnershipObservation is null)
        {
            reason = "Publication metadata must be present as a complete, valid set or not at all.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool HasAnyPublicationField()
    {
        return PublicationToken is not null
            || ActiveImageIdentity is not null
            || PublishedFingerprint is not null
            || RendererVersion is not null
            || LastOwnershipObservation is not null;
    }

    private bool SurfaceMatches(ArtworkImageSurface surface)
    {
        return ImageSurface.Equals(surface);
    }
}
