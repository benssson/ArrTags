using System;
using System.Collections.Generic;
using ArrTags.Media;

namespace ArrTags.Matching;

/// <summary>
/// Applies an ordered sequence of candidate-selection rules to a set of provider
/// candidates and records the evidence for each applied rule. The first rule
/// that matches at least one candidate decides the selection, so later rules act
/// as identity-ordered fallbacks rather than widening the search. The selector
/// never uses title or production year and never accepts a match by itself.
/// </summary>
public static class CandidateSelector
{
    /// <summary>
    /// Selects the provider candidates that satisfy the first matching rule.
    /// </summary>
    /// <param name="identity">The Jellyfin-side subject identity.</param>
    /// <param name="candidates">The provider candidates to evaluate.</param>
    /// <param name="rules">The ordered selection rules.</param>
    /// <returns>The surviving candidates and the evidence recorded for each applied rule.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public static CandidateSelection Select(
        MediaIdentity identity,
        IReadOnlyList<MatchCandidate> candidates,
        IReadOnlyList<CandidateMatchRule> rules)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rules);

        var evidence = new List<MatchEvidence>(rules.Count);
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            var matches = new List<MatchCandidate>();
            string? matchedValue = null;
            foreach (var candidate in candidates)
            {
                if (candidate is not null && rule.IsSatisfiedBy(identity, candidate, out var value))
                {
                    matches.Add(candidate);
                    matchedValue ??= value;
                }
            }

            evidence.Add(new MatchEvidence(rule.Method, rule.EvidenceKey, matchedValue, candidates.Count, matches.Count));

            if (matches.Count > 0)
            {
                return new CandidateSelection(matches, evidence);
            }
        }

        return new CandidateSelection(Array.Empty<MatchCandidate>(), evidence);
    }
}
