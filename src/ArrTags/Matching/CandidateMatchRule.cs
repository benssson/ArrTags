using System;
using ArrTags.Media;

namespace ArrTags.Matching;

/// <summary>
/// One ordered candidate-selection rule. A rule decides whether a provider
/// candidate is satisfied by a Jellyfin identity under one evidence key and
/// returns the matched value for diagnostics. Concrete rule orders for movies,
/// series, and episodes are supplied by the matching policy; a rule never treats
/// title or production year as sole identity.
/// </summary>
public abstract class CandidateMatchRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CandidateMatchRule"/> class.
    /// </summary>
    /// <param name="method">The concrete matching method the rule represents.</param>
    /// <param name="evidenceKey">The non-empty evidence key the rule records, such as a normalized provider identifier name.</param>
    /// <exception cref="ArgumentOutOfRangeException">The method is undefined or <see cref="MediaMatchMethod.None"/>.</exception>
    /// <exception cref="ArgumentException">The evidence key is empty.</exception>
    protected CandidateMatchRule(MediaMatchMethod method, string evidenceKey)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown match method.");
        }

        if (method == MediaMatchMethod.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(method),
                method,
                "A candidate match rule requires a concrete matching method.");
        }

        if (string.IsNullOrWhiteSpace(evidenceKey))
        {
            throw new ArgumentException("A candidate match rule requires a non-empty evidence key.", nameof(evidenceKey));
        }

        Method = method;
        EvidenceKey = evidenceKey;
    }

    /// <summary>
    /// Gets the concrete matching method the rule represents.
    /// </summary>
    public MediaMatchMethod Method { get; }

    /// <summary>
    /// Gets the evidence key the rule records, such as a normalized provider
    /// identifier name.
    /// </summary>
    public string EvidenceKey { get; }

    /// <summary>
    /// Determines whether the candidate is satisfied by the supplied Jellyfin
    /// identity.
    /// </summary>
    /// <param name="identity">The Jellyfin-side subject identity.</param>
    /// <param name="candidate">The provider candidate under evaluation.</param>
    /// <param name="matchedValue">The normalized value that satisfied the rule when it matched.</param>
    /// <returns><see langword="true"/> when the candidate satisfies the rule.</returns>
    public abstract bool IsSatisfiedBy(MediaIdentity identity, MatchCandidate candidate, out string? matchedValue);
}
