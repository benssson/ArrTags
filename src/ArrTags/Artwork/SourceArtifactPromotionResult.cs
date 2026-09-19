namespace ArrTags.Artwork;

/// <summary>
/// Why a source artifact promotion did not succeed. The failures are bounded and
/// non-secret so they can be recorded or logged without leaking source content.
/// </summary>
public enum SourceArtifactPromotionFailure
{
    /// <summary>The promotion succeeded.</summary>
    None,

    /// <summary>The source is empty or otherwise unusable.</summary>
    InvalidContent,

    /// <summary>The source exceeds the configured source-artifact byte limit.</summary>
    TooLarge,

    /// <summary>The declared MIME type is unsupported or does not match the bytes.</summary>
    UnsupportedFormat,

    /// <summary>The declared integrity hash does not match the bytes.</summary>
    HashMismatch,

    /// <summary>Promoting the artifact would exceed the authoritative artifact storage quota.</summary>
    StorageQuotaExceeded,
}

/// <summary>
/// The immutable result of attempting to promote a source artifact into the
/// authoritative content-addressed store.
/// </summary>
public sealed class SourceArtifactPromotionResult
{
    private SourceArtifactPromotionResult(
        bool succeeded,
        SourceArtifactInfo? info,
        SourceArtifactPromotionFailure failure,
        string reason)
    {
        Succeeded = succeeded;
        Info = info;
        Failure = failure;
        Reason = reason;
    }

    /// <summary>
    /// Gets a value indicating whether the artifact is durably promoted.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the artifact metadata when the promotion succeeded.
    /// </summary>
    public SourceArtifactInfo? Info { get; }

    /// <summary>
    /// Gets the failure classification when the promotion did not succeed.
    /// </summary>
    public SourceArtifactPromotionFailure Failure { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="info">The promoted artifact metadata.</param>
    /// <returns>A successful result.</returns>
    public static SourceArtifactPromotionResult Promoted(SourceArtifactInfo info)
    {
        return new SourceArtifactPromotionResult(true, info, SourceArtifactPromotionFailure.None, "The source artifact was promoted.");
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A failed result.</returns>
    public static SourceArtifactPromotionResult Failed(SourceArtifactPromotionFailure failure, string reason)
    {
        return new SourceArtifactPromotionResult(false, null, failure, reason);
    }
}
