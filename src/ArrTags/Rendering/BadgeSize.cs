namespace ArrTags.Rendering;

/// <summary>
/// The global preset badge size (ADR-019, extended by ADR-027). The preset
/// multiplies the existing width-based geometry scale by a code-owned factor:
/// <see cref="Small"/> is <c>0.75</c>, <see cref="Medium"/> is <c>1.0</c>,
/// <see cref="Large"/> is <c>1.5</c>, and <see cref="ExtraLarge"/> is <c>2.0</c>.
/// The effective scale is clamped so the badge still fits the safe area. Size is
/// global renderer policy in v1.1 and is not representable per selector.
/// </summary>
/// <remarks>
/// The numeric value <c>0</c> is <see cref="Medium"/> so the default (an absent
/// or zero-valued persisted field) reproduces the V1 geometry. The numeric
/// values are explicit because the effective size participates in the renderer
/// configuration and render fingerprints.
/// </remarks>
public enum BadgeSize
{
    /// <summary>
    /// The reference V1 geometry. This is the default and reproduces the ADR-009
    /// output.
    /// </summary>
    Medium = 0,

    /// <summary>
    /// A smaller badge, scaling the reference geometry by <c>0.75</c>.
    /// </summary>
    Small = 1,

    /// <summary>
    /// A larger badge, scaling the reference geometry by <c>1.5</c>.
    /// </summary>
    Large = 2,

    /// <summary>
    /// The largest preset badge, scaling the reference geometry by <c>2.0</c>
    /// (twice <see cref="Medium"/>). ADR-027 clause 1.
    /// </summary>
    ExtraLarge = 3,
}
