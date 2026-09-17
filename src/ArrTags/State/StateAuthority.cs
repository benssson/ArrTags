namespace ArrTags.State;

/// <summary>
/// Distinguishes ordinary, rebuildable cache state from authoritative
/// artwork-operation and provenance state.
/// </summary>
public enum StateAuthority
{
    /// <summary>
    /// Rebuildable cache state. An invalid record may be discarded.
    /// </summary>
    Cache,

    /// <summary>
    /// Authoritative state. An invalid record is quarantined and never treated
    /// as absent.
    /// </summary>
    Authoritative,
}
