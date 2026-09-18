namespace ArrTags.Rendering;

/// <summary>
/// The placement role of a resolved badge value. Technical values populate the
/// bottom-left rail; the status value is the independent top-right pill.
/// </summary>
public enum BadgeValueKind
{
    /// <summary>
    /// A technical badge placed in the priority-ordered bottom-left rail.
    /// </summary>
    Technical,

    /// <summary>
    /// The separate upgrade-status badge shown at the top-right.
    /// </summary>
    Status,
}
