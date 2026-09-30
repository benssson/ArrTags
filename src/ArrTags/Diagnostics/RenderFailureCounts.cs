namespace ArrTags.Diagnostics;

/// <summary>
/// The bounded process-lifetime render-failure counters of the diagnostics
/// snapshot (ADR-025 clause 3), one count per <see cref="ArrTags.Rendering.RenderFailureReason"/>
/// classification. The type is a fixed-shape record of counts only: it carries
/// no reason text, item identity, item name, path, provider payload, credential,
/// or unbounded collection.
/// </summary>
public sealed class RenderFailureCounts
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderFailureCounts"/> class
    /// with one count per <see cref="ArrTags.Rendering.RenderFailureReason"/>
    /// value, in the enum's declared order.
    /// </summary>
    /// <param name="invalidRequest">The count of <see cref="ArrTags.Rendering.RenderFailureReason.InvalidRequest"/> failures.</param>
    /// <param name="malformedSource">The count of <see cref="ArrTags.Rendering.RenderFailureReason.MalformedSource"/> failures.</param>
    /// <param name="sourceByteLimitExceeded">The count of <see cref="ArrTags.Rendering.RenderFailureReason.SourceByteLimitExceeded"/> failures.</param>
    /// <param name="sourceDimensionLimitExceeded">The count of <see cref="ArrTags.Rendering.RenderFailureReason.SourceDimensionLimitExceeded"/> failures.</param>
    /// <param name="outputDimensionLimitExceeded">The count of <see cref="ArrTags.Rendering.RenderFailureReason.OutputDimensionLimitExceeded"/> failures.</param>
    /// <param name="outputByteLimitExceeded">The count of <see cref="ArrTags.Rendering.RenderFailureReason.OutputByteLimitExceeded"/> failures.</param>
    /// <param name="colorPolicyInvalid">The count of <see cref="ArrTags.Rendering.RenderFailureReason.ColorPolicyInvalid"/> failures.</param>
    /// <param name="contrastTooLow">The count of <see cref="ArrTags.Rendering.RenderFailureReason.ContrastTooLow"/> failures.</param>
    /// <param name="decodeFailed">The count of <see cref="ArrTags.Rendering.RenderFailureReason.DecodeFailed"/> failures.</param>
    /// <param name="unsupportedInput">The count of <see cref="ArrTags.Rendering.RenderFailureReason.UnsupportedInput"/> failures.</param>
    /// <param name="unsupportedColorProfile">The count of <see cref="ArrTags.Rendering.RenderFailureReason.UnsupportedColorProfile"/> failures.</param>
    /// <param name="fontUnavailable">The count of <see cref="ArrTags.Rendering.RenderFailureReason.FontUnavailable"/> failures.</param>
    /// <param name="fontInvalid">The count of <see cref="ArrTags.Rendering.RenderFailureReason.FontInvalid"/> failures.</param>
    /// <param name="layoutFailed">The count of <see cref="ArrTags.Rendering.RenderFailureReason.LayoutFailed"/> failures.</param>
    /// <param name="encodeFailed">The count of <see cref="ArrTags.Rendering.RenderFailureReason.EncodeFailed"/> failures.</param>
    /// <param name="nativeAssetUnavailable">The count of <see cref="ArrTags.Rendering.RenderFailureReason.NativeAssetUnavailable"/> failures.</param>
    /// <param name="renderError">The count of <see cref="ArrTags.Rendering.RenderFailureReason.RenderError"/> failures.</param>
    /// <param name="cancelled">The count of <see cref="ArrTags.Rendering.RenderFailureReason.Cancelled"/> failures.</param>
    public RenderFailureCounts(
        long invalidRequest,
        long malformedSource,
        long sourceByteLimitExceeded,
        long sourceDimensionLimitExceeded,
        long outputDimensionLimitExceeded,
        long outputByteLimitExceeded,
        long colorPolicyInvalid,
        long contrastTooLow,
        long decodeFailed,
        long unsupportedInput,
        long unsupportedColorProfile,
        long fontUnavailable,
        long fontInvalid,
        long layoutFailed,
        long encodeFailed,
        long nativeAssetUnavailable,
        long renderError,
        long cancelled)
    {
        InvalidRequest = invalidRequest;
        MalformedSource = malformedSource;
        SourceByteLimitExceeded = sourceByteLimitExceeded;
        SourceDimensionLimitExceeded = sourceDimensionLimitExceeded;
        OutputDimensionLimitExceeded = outputDimensionLimitExceeded;
        OutputByteLimitExceeded = outputByteLimitExceeded;
        ColorPolicyInvalid = colorPolicyInvalid;
        ContrastTooLow = contrastTooLow;
        DecodeFailed = decodeFailed;
        UnsupportedInput = unsupportedInput;
        UnsupportedColorProfile = unsupportedColorProfile;
        FontUnavailable = fontUnavailable;
        FontInvalid = fontInvalid;
        LayoutFailed = layoutFailed;
        EncodeFailed = encodeFailed;
        NativeAssetUnavailable = nativeAssetUnavailable;
        RenderError = renderError;
        Cancelled = cancelled;
    }

    /// <summary>
    /// Gets the process-lifetime count of structurally unusable render requests.
    /// </summary>
    public long InvalidRequest { get; }

    /// <summary>
    /// Gets the process-lifetime count of malformed source descriptors.
    /// </summary>
    public long MalformedSource { get; }

    /// <summary>
    /// Gets the process-lifetime count of source byte-length limit failures.
    /// </summary>
    public long SourceByteLimitExceeded { get; }

    /// <summary>
    /// Gets the process-lifetime count of source per-side dimension limit failures.
    /// </summary>
    public long SourceDimensionLimitExceeded { get; }

    /// <summary>
    /// Gets the process-lifetime count of planned output per-side dimension limit failures.
    /// </summary>
    public long OutputDimensionLimitExceeded { get; }

    /// <summary>
    /// Gets the process-lifetime count of derived-artifact byte limit failures.
    /// </summary>
    public long OutputByteLimitExceeded { get; }

    /// <summary>
    /// Gets the process-lifetime count of missing or unparseable style colors.
    /// </summary>
    public long ColorPolicyInvalid { get; }

    /// <summary>
    /// Gets the process-lifetime count of contrast-ratio failures.
    /// </summary>
    public long ContrastTooLow { get; }

    /// <summary>
    /// Gets the process-lifetime count of source decode failures.
    /// </summary>
    public long DecodeFailed { get; }

    /// <summary>
    /// Gets the process-lifetime count of unsupported source image semantics.
    /// </summary>
    public long UnsupportedInput { get; }

    /// <summary>
    /// Gets the process-lifetime count of unsupported embedded color profiles.
    /// </summary>
    public long UnsupportedColorProfile { get; }

    /// <summary>
    /// Gets the process-lifetime count of missing bundled-font failures.
    /// </summary>
    public long FontUnavailable { get; }

    /// <summary>
    /// Gets the process-lifetime count of unusable bundled-font failures.
    /// </summary>
    public long FontInvalid { get; }

    /// <summary>
    /// Gets the process-lifetime count of layout-stage failures.
    /// </summary>
    public long LayoutFailed { get; }

    /// <summary>
    /// Gets the process-lifetime count of PNG encoder failures.
    /// </summary>
    public long EncodeFailed { get; }

    /// <summary>
    /// Gets the process-lifetime count of unavailable native renderer assets.
    /// </summary>
    public long NativeAssetUnavailable { get; }

    /// <summary>
    /// Gets the process-lifetime count of unclassified renderer errors.
    /// </summary>
    public long RenderError { get; }

    /// <summary>
    /// Gets the process-lifetime count of cancelled render attempts.
    /// </summary>
    public long Cancelled { get; }
}
