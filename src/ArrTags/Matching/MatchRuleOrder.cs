using System;
using System.Collections.Generic;
using ArrTags.Media;
using ArrTags.Providers;

namespace ArrTags.Matching;

/// <summary>
/// The documented, provider-neutral matching order for each supported Jellyfin
/// item type. The order is expressed as an ordered list of
/// <see cref="CandidateMatchRule"/> instances: the first rule that matches any
/// candidate decides the selection, so later rules are identity fallbacks rather
/// than a widened search.
/// </summary>
/// <remarks>
/// The accepted order (see <c>docs/architecture.md</c> section 7, "Matching
/// policy") is:
/// <list type="bullet">
/// <item>Movie to Radarr: TMDb id, then IMDb id.</item>
/// <item>Series to Sonarr: TVDB id, then the other stable provider ids Sonarr
/// exposes locally (TMDb, then IMDb).</item>
/// <item>Episode to Sonarr: episode TVDB id. Exact season/episode number
/// fallback is deliberately not enabled here because the numbering policy is
/// decision gate DG-4 and is implemented by task 3.5; configured path fallback
/// is task 3.6.</item>
/// </list>
/// Title and production year are never rules. Season and cross-provider
/// combinations are unsupported for automatic matching.
/// </remarks>
public static class MatchRuleOrder
{
    private static readonly IReadOnlyList<CandidateMatchRule> MovieToRadarrRules =
        Array.AsReadOnly<CandidateMatchRule>(new CandidateMatchRule[]
        {
            new ProviderIdMatchRule(MatchProviderIdKeys.Tmdb),
            new ProviderIdMatchRule(MatchProviderIdKeys.Imdb),
        });

    private static readonly IReadOnlyList<CandidateMatchRule> SeriesToSonarrRules =
        Array.AsReadOnly<CandidateMatchRule>(new CandidateMatchRule[]
        {
            new ProviderIdMatchRule(MatchProviderIdKeys.Tvdb),
            new ProviderIdMatchRule(MatchProviderIdKeys.Tmdb),
            new ProviderIdMatchRule(MatchProviderIdKeys.Imdb),
        });

    private static readonly IReadOnlyList<CandidateMatchRule> EpisodeToSonarrRules =
        Array.AsReadOnly<CandidateMatchRule>(new CandidateMatchRule[]
        {
            new ProviderIdMatchRule(MatchProviderIdKeys.Tvdb),
        });

    /// <summary>
    /// Gets the documented ordered rules for an item type and provider kind.
    /// </summary>
    /// <param name="itemType">The Jellyfin structural item type.</param>
    /// <param name="providerKind">The Arr provider family.</param>
    /// <param name="rules">The ordered rules when the combination is supported; otherwise an empty list.</param>
    /// <returns><see langword="true"/> when the item type and provider kind are a supported automatic-matching pair.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The item type or provider kind is not defined.</exception>
    public static bool TryGetRules(
        MediaItemType itemType,
        ArrProviderKind providerKind,
        out IReadOnlyList<CandidateMatchRule> rules)
    {
        if (!Enum.IsDefined(itemType))
        {
            throw new ArgumentOutOfRangeException(nameof(itemType), itemType, "Unknown media item type.");
        }

        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind), providerKind, "Unknown Arr provider kind.");
        }

        rules = (itemType, providerKind) switch
        {
            (MediaItemType.Movie, ArrProviderKind.Radarr) => MovieToRadarrRules,
            (MediaItemType.Series, ArrProviderKind.Sonarr) => SeriesToSonarrRules,
            (MediaItemType.Episode, ArrProviderKind.Sonarr) => EpisodeToSonarrRules,
            _ => Array.Empty<CandidateMatchRule>(),
        };

        return rules.Count > 0;
    }
}
