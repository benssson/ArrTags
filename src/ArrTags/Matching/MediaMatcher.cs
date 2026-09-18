using System;
using System.Collections.Generic;
using ArrTags.Media;
using ArrTags.Providers;

namespace ArrTags.Matching;

/// <summary>
/// The provider-neutral matching entry point. It applies the documented matching
/// order for a Jellyfin item type to a set of connection-scoped
/// <see cref="MatchCandidate"/> records and resolves the result into a canonical
/// <see cref="MediaMatch"/>. Unsupported item/provider combinations and episodes
/// without a matched parent series produce bounded, non-matched outcomes rather
/// than a guessed match.
/// </summary>
public static class MediaMatcher
{
    private const string UnsupportedReason =
        "The item type is not supported for the configured provider.";

    private const string MissingSeriesContextReason =
        "An episode match requires parent series context.";

    private const string SeriesNotMatchedReason =
        "The parent series did not match, so the episode was not matched.";

    /// <summary>
    /// Matches one Jellyfin item against provider candidates using the documented
    /// rule order for the item type and provider kind.
    /// </summary>
    /// <param name="identity">The Jellyfin-side subject identity.</param>
    /// <param name="provider">The provider identity the match is scoped to.</param>
    /// <param name="connectionId">The connection scope for the resolved local identifiers.</param>
    /// <param name="candidates">The connection-scoped provider candidates.</param>
    /// <param name="matchedAt">When the match was last validated, when known.</param>
    /// <returns>The canonical match. An unsupported item/provider combination produces <see cref="MediaMatchStatus.Unsupported"/>.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public static MediaMatch Match(
        MediaIdentity identity,
        ArrProvider provider,
        ArrConnectionId connectionId,
        IReadOnlyList<MatchCandidate> candidates,
        DateTimeOffset? matchedAt = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(candidates);

        if (!MatchRuleOrder.TryGetRules(identity.ItemType, provider.Kind, out var rules))
        {
            return new MediaMatch(
                identity,
                provider,
                connectionId,
                MediaMatchStatus.Unsupported,
                MediaMatchMethod.None,
                ambiguityReason: UnsupportedReason,
                matchedAt: matchedAt);
        }

        if (IsBadgeSurface(identity.ItemType)
            && MediaLocationEligibility.TryGetIneligibleReason(identity, out var locationReason))
        {
            return new MediaMatch(
                identity,
                provider,
                connectionId,
                MediaMatchStatus.Unsupported,
                MediaMatchMethod.None,
                ambiguityReason: locationReason,
                matchedAt: matchedAt);
        }

        var selection = CandidateSelector.Select(identity, candidates, rules);
        return MediaMatchPolicy.Resolve(identity, provider, connectionId, selection, matchedAt);
    }

    /// <summary>
    /// Matches a Jellyfin episode by first matching its parent series and then
    /// evaluating only the episode candidates that belong to that matched Sonarr
    /// series. The parent series match is required and is never inferred from the
    /// episode alone.
    /// </summary>
    /// <param name="episodeIdentity">The Jellyfin episode identity; it must carry parent series context.</param>
    /// <param name="provider">The Sonarr provider identity the match is scoped to.</param>
    /// <param name="connectionId">The connection scope for the resolved local identifiers.</param>
    /// <param name="seriesCandidates">The series candidates considered for the parent series match.</param>
    /// <param name="episodeCandidates">The episode candidates, filtered to the matched series before evaluation.</param>
    /// <param name="matchedAt">When the match was last validated, when known.</param>
    /// <returns>
    /// A matched episode result, or the parent series' <see cref="MediaMatchStatus.NotFound"/>
    /// or <see cref="MediaMatchStatus.Ambiguous"/> outcome when the series does not
    /// match uniquely. <see cref="MediaMatchStatus.Unsupported"/> is produced for
    /// non-Sonarr providers or an episode without series context.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public static MediaMatch MatchEpisode(
        MediaIdentity episodeIdentity,
        ArrProvider provider,
        ArrConnectionId connectionId,
        IReadOnlyList<MatchCandidate> seriesCandidates,
        IReadOnlyList<MatchCandidate> episodeCandidates,
        DateTimeOffset? matchedAt = null)
    {
        ArgumentNullException.ThrowIfNull(episodeIdentity);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(seriesCandidates);
        ArgumentNullException.ThrowIfNull(episodeCandidates);

        if (episodeIdentity.ItemType != MediaItemType.Episode
            || !MatchRuleOrder.TryGetRules(episodeIdentity.ItemType, provider.Kind, out _))
        {
            return new MediaMatch(
                episodeIdentity,
                provider,
                connectionId,
                MediaMatchStatus.Unsupported,
                MediaMatchMethod.None,
                ambiguityReason: UnsupportedReason,
                matchedAt: matchedAt);
        }

        if (MediaLocationEligibility.TryGetIneligibleReason(episodeIdentity, out var locationReason))
        {
            return new MediaMatch(
                episodeIdentity,
                provider,
                connectionId,
                MediaMatchStatus.Unsupported,
                MediaMatchMethod.None,
                ambiguityReason: locationReason,
                matchedAt: matchedAt);
        }

        if (episodeIdentity.SeriesIdentity is not MediaIdentity seriesIdentity)
        {
            return new MediaMatch(
                episodeIdentity,
                provider,
                connectionId,
                MediaMatchStatus.Unsupported,
                MediaMatchMethod.None,
                ambiguityReason: MissingSeriesContextReason,
                matchedAt: matchedAt);
        }

        var seriesMatch = Match(seriesIdentity, provider, connectionId, seriesCandidates, matchedAt);
        if (seriesMatch.Status != MediaMatchStatus.Matched)
        {
            return new MediaMatch(
                episodeIdentity,
                provider,
                connectionId,
                seriesMatch.Status,
                MediaMatchMethod.None,
                ambiguityReason: SeriesNotMatchedReason,
                matchedAt: matchedAt);
        }

        if (seriesMatch.RecordIdentity is not SonarrIdentity seriesRecord)
        {
            return new MediaMatch(
                episodeIdentity,
                provider,
                connectionId,
                MediaMatchStatus.Unsupported,
                MediaMatchMethod.None,
                ambiguityReason: UnsupportedReason,
                matchedAt: matchedAt);
        }

        var scopedCandidates = FilterToSeries(episodeCandidates, seriesRecord.SeriesId);
        return Match(episodeIdentity, provider, connectionId, scopedCandidates, matchedAt);
    }

    private static bool IsBadgeSurface(MediaItemType itemType)
    {
        return itemType is MediaItemType.Movie or MediaItemType.Episode;
    }

    private static IReadOnlyList<MatchCandidate> FilterToSeries(
        IReadOnlyList<MatchCandidate> candidates,
        int seriesId)
    {
        var scoped = new List<MatchCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (candidate is not null
                && candidate.RecordIdentity is SonarrIdentity record
                && record.EpisodeId is not null
                && record.SeriesId == seriesId)
            {
                scoped.Add(candidate);
            }
        }

        return scoped;
    }
}
