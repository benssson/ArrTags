namespace ArrTags.Artwork;

/// <summary>
/// The bounded outcome of one single-subject publication attempt. Every
/// non-<see cref="Published"/> value means the currently active artwork was left
/// unchanged and the durable operation was not marked committed. The later
/// reconciliation task (5.7) owns resolving the uncertainty for the outcomes
/// that occur after an external mutation may have started.
/// </summary>
public enum ArtworkPublicationOutcome
{
    /// <summary>The derived image was published and the final artwork state was committed.</summary>
    Published,

    /// <summary>The request was not a valid bounded V1 publication request.</summary>
    InvalidRequest,

    /// <summary>The current artwork state does not permit publication (blocked or restore-pending).</summary>
    NotEligible,

    /// <summary>An unresolved durable operation or invalid authoritative record blocks new work.</summary>
    Blocked,

    /// <summary>The current source surface could not be read.</summary>
    SourceUnavailable,

    /// <summary>The source baseline could not be retained under the authoritative limits.</summary>
    SourceRejected,

    /// <summary>The render artifact failed the bounded integrity or limit checks.</summary>
    DerivedArtifactRejected,

    /// <summary>The captured source baseline is no longer current; a new capture is required.</summary>
    RecaptureRequired,

    /// <summary>The before identity changed before the mutation; no image call was made.</summary>
    BeforeIdentityChanged,

    /// <summary>The before identity could not be observed before the mutation; no image call was made.</summary>
    BeforeIdentityUnknown,

    /// <summary>The supported image save call did not complete.</summary>
    ImageMutationFailed,

    /// <summary>The normal item update did not complete after the image save.</summary>
    RepositoryUpdateFailed,

    /// <summary>The effective active image could not be observed after the mutation.</summary>
    ReadbackUnknown,

    /// <summary>The observed active image does not match the candidate publication.</summary>
    ReadbackMismatch,

    /// <summary>The publication was cancelled; no further automatic mutation is attempted.</summary>
    Cancelled,
}
