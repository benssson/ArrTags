namespace ArrTags.Matching;

/// <summary>
/// The bounded outcome of matching one Jellyfin item to a provider record. Only
/// <see cref="Matched"/> permits provider metadata to be used for a badge; every
/// other state produces no new badge and a safe diagnostic reason.
/// </summary>
public enum MediaMatchStatus
{
    /// <summary>
    /// The item matched exactly one provider record with validated identity
    /// evidence.
    /// </summary>
    Matched,

    /// <summary>
    /// No provider record satisfied the matching policy.
    /// </summary>
    NotFound,

    /// <summary>
    /// More than one provider record remained, so no automatic match is
    /// accepted.
    /// </summary>
    Ambiguous,

    /// <summary>
    /// The item or provider scope is not supported for matching.
    /// </summary>
    Unsupported,

    /// <summary>
    /// A previously validated association is no longer confirmed against current
    /// provider state.
    /// </summary>
    Stale,
}
