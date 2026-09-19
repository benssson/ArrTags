namespace ArrTags.Rendering;

/// <summary>
/// The bounded, non-secret reason a render passes through and leaves the current
/// artwork unchanged. A pass-through is a normal outcome, not a failure, and it
/// never carries a partial artifact or a provider payload.
/// </summary>
public enum RenderPassThroughReason
{
    /// <summary>
    /// No canonical metadata observation was supplied.
    /// </summary>
    NoMetadata,

    /// <summary>
    /// The metadata produced no displayable value after selection and layout.
    /// </summary>
    NoDisplayableValue,

    /// <summary>
    /// The item is not a V1 badge-bearing Movie or Episode poster surface.
    /// </summary>
    IneligibleSurface,

    /// <summary>
    /// The match is not an eligible <c>Matched</c> result, so no metadata may be
    /// displayed.
    /// </summary>
    MatchNotEligible,

    /// <summary>
    /// No source descriptor was available, so the source cannot be rendered.
    /// </summary>
    SourceUnavailable,
}
