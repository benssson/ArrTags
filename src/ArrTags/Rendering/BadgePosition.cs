namespace ArrTags.Rendering;

/// <summary>
/// The global anchor of the technical badge rail (ADR-019). It positions the
/// rail only; the independent <c>UPGRADE</c> status pill is always top-right
/// except when the rail anchor is <see cref="TopRight"/>, in which case the
/// status pill is top-left so the two never overlap. Placement is global
/// renderer policy in v1.1 and is not representable per selector.
/// </summary>
/// <remarks>
/// The numeric value <c>0</c> is <see cref="BottomLeft"/> so the default (an
/// absent or zero-valued persisted field) reproduces the V1 output. The numeric
/// values are explicit because the effective position participates in the
/// renderer configuration and render fingerprints.
/// </remarks>
public enum BadgePosition
{
    /// <summary>
    /// The rail is anchored bottom-left and stacks upward. This is the V1
    /// default and reproduces the ADR-009 output.
    /// </summary>
    BottomLeft = 0,

    /// <summary>
    /// The rail is anchored top-left and stacks downward.
    /// </summary>
    TopLeft = 1,

    /// <summary>
    /// The rail is anchored top-right and stacks downward. Because the rail
    /// occupies the top-right, the status pill moves to the top-left.
    /// </summary>
    TopRight = 2,

    /// <summary>
    /// The rail is anchored bottom-right and stacks upward.
    /// </summary>
    BottomRight = 3,

    /// <summary>
    /// The rail is vertically and horizontally centered.
    /// </summary>
    Center = 4,
}
