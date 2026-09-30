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
    /// The resolved badge selection was empty: no enabled selector produced a
    /// value (for example a selector allowlist that excludes every resolved
    /// value). For an owned published session this reason is a restore
    /// obligation rather than a preserve (ADR-024).
    /// </summary>
    NoDisplayableValue,

    /// <summary>
    /// The resolved badge selection was not empty, but nothing fit the safe area
    /// after shortening and omission, so no badge was drawn. The current artwork
    /// is preserved: a valid value that merely failed layout never removes a
    /// correct ArrTags image.
    /// </summary>
    NoFittingBadge,

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
