using System;
using System.Collections.Generic;
using System.Globalization;
using ArrTags.Media;
using ArrTags.Providers;

namespace ArrTags.Matching;

/// <summary>
/// Converts a provider-neutral <see cref="CandidateSelection"/> into the bounded
/// canonical <see cref="MediaMatch"/> result. Exactly one surviving candidate is
/// accepted; zero survivors produce <see cref="MediaMatchStatus.NotFound"/> and
/// multiple survivors produce <see cref="MediaMatchStatus.Ambiguous"/>. It never
/// resolves a match from a candidate title or production year.
/// </summary>
public static class MediaMatchPolicy
{
    private const string NotFoundReason =
        "No provider record satisfied the configured identity rules.";

    /// <summary>
    /// Resolves a candidate selection into a canonical match result.
    /// </summary>
    /// <param name="mediaIdentity">The Jellyfin-side subject being matched.</param>
    /// <param name="provider">The provider identity the match is scoped to.</param>
    /// <param name="connectionId">The connection scope for the resolved local identifiers.</param>
    /// <param name="selection">The candidate selection produced by <see cref="CandidateSelector"/>.</param>
    /// <param name="matchedAt">When the match was last validated, when known.</param>
    /// <returns>
    /// A <see cref="MediaMatchStatus.Matched"/> result only when exactly one
    /// candidate survived. Zero survivors return
    /// <see cref="MediaMatchStatus.NotFound"/> and multiple survivors return
    /// <see cref="MediaMatchStatus.Ambiguous"/>; neither carries a record
    /// identity.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public static MediaMatch Resolve(
        MediaIdentity mediaIdentity,
        ArrProvider provider,
        ArrConnectionId connectionId,
        CandidateSelection selection,
        DateTimeOffset? matchedAt = null)
    {
        ArgumentNullException.ThrowIfNull(mediaIdentity);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsEmpty)
        {
            return new MediaMatch(
                mediaIdentity,
                provider,
                connectionId,
                MediaMatchStatus.NotFound,
                MediaMatchMethod.None,
                ambiguityReason: NotFoundReason,
                matchedAt: matchedAt);
        }

        if (selection.IsAmbiguous)
        {
            return new MediaMatch(
                mediaIdentity,
                provider,
                connectionId,
                MediaMatchStatus.Ambiguous,
                MediaMatchMethod.None,
                ambiguityReason: CreateAmbiguousReason(selection.SurvivorCount),
                matchedAt: matchedAt);
        }

        var candidate = selection.UniqueCandidate!;
        var evidence = selection.DecidingEvidence!;

        return new MediaMatch(
            mediaIdentity,
            provider,
            connectionId,
            MediaMatchStatus.Matched,
            evidence.Method,
            candidate.RecordIdentity,
            CreateMatchedProviderIds(evidence),
            matchedAt: matchedAt);
    }

    private static string CreateAmbiguousReason(int survivorCount)
    {
        return "Multiple provider records ("
            + survivorCount.ToString(CultureInfo.InvariantCulture)
            + ") satisfied the configured identity rules.";
    }

    private static IReadOnlyDictionary<string, string>? CreateMatchedProviderIds(MatchEvidence evidence)
    {
        if (evidence.Method != MediaMatchMethod.ProviderId
            || string.IsNullOrWhiteSpace(evidence.MatchedValue))
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [evidence.EvidenceKey] = evidence.MatchedValue,
        };
    }
}
