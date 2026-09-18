using System;
using System.Linq;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.4 checks for the documented movie, series, and episode matching order.
/// The order is provider-neutral identity rules only; exact episode-number and
/// configured-path fallbacks remain disabled pending tasks 3.5 and 3.6. These
/// tests require no live Jellyfin or Arr instance.
/// </summary>
public class MatchRuleOrderTests
{
    [Fact]
    public void MovieToRadarrUsesTmdbThenImdb()
    {
        Assert.True(MatchRuleOrder.TryGetRules(MediaItemType.Movie, ArrProviderKind.Radarr, out var rules));

        Assert.Equal(
            new[] { MatchProviderIdKeys.Tmdb, MatchProviderIdKeys.Imdb },
            rules.Select(rule => rule.EvidenceKey));
        Assert.All(rules, rule => Assert.Equal(MediaMatchMethod.ProviderId, rule.Method));
    }

    [Fact]
    public void SeriesToSonarrUsesTvdbThenOtherStableProviderIds()
    {
        Assert.True(MatchRuleOrder.TryGetRules(MediaItemType.Series, ArrProviderKind.Sonarr, out var rules));

        Assert.Equal(
            new[] { MatchProviderIdKeys.Tvdb, MatchProviderIdKeys.Tmdb, MatchProviderIdKeys.Imdb },
            rules.Select(rule => rule.EvidenceKey));
        Assert.All(rules, rule => Assert.Equal(MediaMatchMethod.ProviderId, rule.Method));
    }

    [Fact]
    public void EpisodeToSonarrUsesEpisodeTvdbOnlyUntilNumberingPolicyIsApproved()
    {
        Assert.True(MatchRuleOrder.TryGetRules(MediaItemType.Episode, ArrProviderKind.Sonarr, out var rules));

        var rule = Assert.Single(rules);
        Assert.Equal(MatchProviderIdKeys.Tvdb, rule.EvidenceKey);
        Assert.Equal(MediaMatchMethod.ProviderId, rule.Method);
        Assert.DoesNotContain(rules, candidate => candidate.Method == MediaMatchMethod.Number);
        Assert.DoesNotContain(rules, candidate => candidate.Method == MediaMatchMethod.ConfiguredPath);
    }

    [Theory]
    [InlineData(MediaItemType.Movie, ArrProviderKind.Sonarr)]
    [InlineData(MediaItemType.Series, ArrProviderKind.Radarr)]
    [InlineData(MediaItemType.Season, ArrProviderKind.Sonarr)]
    [InlineData(MediaItemType.Season, ArrProviderKind.Radarr)]
    [InlineData(MediaItemType.Episode, ArrProviderKind.Radarr)]
    public void CrossProviderAndStructuralCombinationsAreUnsupported(MediaItemType itemType, ArrProviderKind providerKind)
    {
        Assert.False(MatchRuleOrder.TryGetRules(itemType, providerKind, out var rules));
        Assert.Empty(rules);
    }

    [Theory]
    [InlineData(MediaItemType.Movie, ArrProviderKind.Radarr)]
    [InlineData(MediaItemType.Series, ArrProviderKind.Sonarr)]
    [InlineData(MediaItemType.Episode, ArrProviderKind.Sonarr)]
    public void SupportedCombinationsReturnRules(MediaItemType itemType, ArrProviderKind providerKind)
    {
        Assert.True(MatchRuleOrder.TryGetRules(itemType, providerKind, out var rules));
        Assert.NotEmpty(rules);
    }

    [Fact]
    public void UndefinedItemTypeOrProviderKindIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MatchRuleOrder.TryGetRules((MediaItemType)99, ArrProviderKind.Sonarr, out _));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MatchRuleOrder.TryGetRules(MediaItemType.Movie, (ArrProviderKind)99, out _));
    }

    [Fact]
    public void RuleOrderIsStableAcrossCalls()
    {
        Assert.True(MatchRuleOrder.TryGetRules(MediaItemType.Movie, ArrProviderKind.Radarr, out var first));
        Assert.True(MatchRuleOrder.TryGetRules(MediaItemType.Movie, ArrProviderKind.Radarr, out var second));

        Assert.Same(first, second);
    }
}
