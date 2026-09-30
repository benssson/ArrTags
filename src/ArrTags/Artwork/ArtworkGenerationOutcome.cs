namespace ArrTags.Artwork;

/// <summary>
/// The bounded outcome of one artwork generation attempt.
/// <see cref="Published"/> means the derived image became the active artwork,
/// and <see cref="Restored"/> means an owned session's empty resolved selection
/// completed the guarded restoration (the retained baseline replaced the
/// ArrTags image, or the ArrTags image was removed because the baseline was
/// absent). Every other value means no image mutation completed in this attempt,
/// so the currently usable artwork is preserved. The values classify where
/// generation stopped: the source could not supply a render, the render decided
/// to pass through or failed, an eligible render was not published, or a
/// restoration obligation did not complete. Each value maps to a safe diagnostic
/// and never to a partial artifact, provider payload, path, or credential.
/// </summary>
public enum ArtworkGenerationOutcome
{
    /// <summary>The derived image was rendered and published as the active artwork.</summary>
    Published,

    /// <summary>
    /// An owned session's empty resolved selection completed the guarded
    /// restoration: the retained source baseline replaced the ArrTags image, or
    /// the ArrTags image was removed because the baseline was absent. The
    /// result's <c>ReconciliationOutcome</c> is
    /// <see cref="ArtworkReconciliationOutcome.Completed"/>.
    /// </summary>
    Restored,

    /// <summary>The surface has no source image, so there was nothing to render.</summary>
    NoSource,

    /// <summary>The source read could not supply a usable image (unavailable, unsupported, or oversized).</summary>
    SourceUnavailable,

    /// <summary>The renderer passed through (for example missing or ineligible metadata); no publication was attempted.</summary>
    RenderPassThrough,

    /// <summary>The renderer failed and produced no artifact; no publication was attempted.</summary>
    RenderFailed,

    /// <summary>A complete render was produced but publication did not complete; the active artwork is unchanged.</summary>
    PublicationNotCompleted,

    /// <summary>The attempt was blocked before an eligible render could complete (invalid bounded input or a blocked subject).</summary>
    Blocked,

    /// <summary>The attempt was cancelled; no partial artifact or publication occurred.</summary>
    Cancelled,
}
