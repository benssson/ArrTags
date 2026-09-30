namespace ArrTags.Diagnostics;

/// <summary>
/// The bounded process-lifetime matching-failure counters of the diagnostics
/// snapshot (ADR-025 clause 3), one count per non-matched
/// <see cref="ArrTags.Matching.MediaMatchStatus"/> classification. The type is a
/// fixed-shape record of counts only: it carries no item identity, item name,
/// path, provider payload, credential, or unbounded collection.
/// </summary>
public sealed class MatchingFailureCounts
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MatchingFailureCounts"/> class.
    /// </summary>
    /// <param name="notFound">The process-lifetime count of <see cref="ArrTags.Matching.MediaMatchStatus.NotFound"/> outcomes.</param>
    /// <param name="ambiguous">The process-lifetime count of <see cref="ArrTags.Matching.MediaMatchStatus.Ambiguous"/> outcomes.</param>
    /// <param name="unsupported">The process-lifetime count of <see cref="ArrTags.Matching.MediaMatchStatus.Unsupported"/> outcomes.</param>
    public MatchingFailureCounts(long notFound, long ambiguous, long unsupported)
    {
        NotFound = notFound;
        Ambiguous = ambiguous;
        Unsupported = unsupported;
    }

    /// <summary>
    /// Gets the process-lifetime count of matching outcomes with no candidate
    /// match.
    /// </summary>
    public long NotFound { get; }

    /// <summary>
    /// Gets the process-lifetime count of matching outcomes with more than one
    /// candidate, where no automatic match is accepted.
    /// </summary>
    public long Ambiguous { get; }

    /// <summary>
    /// Gets the process-lifetime count of matching outcomes the item or provider
    /// scope does not support.
    /// </summary>
    public long Unsupported { get; }
}
