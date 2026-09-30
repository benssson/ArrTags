namespace ArrTags.Media;

/// <summary>
/// The bounded outcome of an identity-anchored resume request against the
/// candidate enumeration order (ADR-029 clause 2). The host provides no
/// keyset/after-key predicate, so the resume walks the host-ordered pages and
/// either locates the anchor or reports that it cannot be located (in which case
/// the caller resets to the start per ADR-029 clause 4).
/// </summary>
public enum MediaLibraryResumeOutcome
{
    /// <summary>
    /// The anchor item was located in the host order, and the result carries the
    /// page that begins strictly after it.
    /// </summary>
    Located,

    /// <summary>
    /// The anchor item is not present in the current host order (it was removed,
    /// became ineligible, or the order changed), so the caller resets to the
    /// start.
    /// </summary>
    AnchorNotFound,
}
