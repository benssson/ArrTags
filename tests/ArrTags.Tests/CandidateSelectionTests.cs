using System;
using System.Collections.Generic;
using ArrTags.Media;
using ArrTags.Matching;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.2 checks for canonical candidate selection and evidence recording.
/// These tests require no live Jellyfin or Arr instance.
/// </summary>
public class CandidateSelectionTests
{
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly ArrConnectionId SonarrConnection =
        ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:8989");
    private static readonly ArrConnectionId OtherSonarrConnection =
        ArrConnectionId.For(ArrProviderKind.Sonarr, "http://other-sonarr.local:8989");

    [Fact]
    public void CandidateRejectsRecordIdentityFromAnotherConnectionScope()
    {
        var identity = new SonarrIdentity(SonarrConnection, 10);

        var exception = Assert.Throws<ArgumentException>(() => new MatchCandidate(
            OtherSonarrConnection,
            ArrProviderKind.Sonarr,
            identity));

        Assert.Equal("recordIdentity", exception.ParamName);
    }

    [Fact]
    public void CandidateRejectsRecordIdentityForDifferentProviderKind()
    {
        var identity = new SonarrIdentity(SonarrConnection, 10);

        var exception = Assert.Throws<ArgumentException>(() => new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Radarr,
            identity));

        Assert.Equal("recordIdentity", exception.ParamName);
    }

    [Fact]
    public void CandidateNormalizesProviderIdentifiers()
    {
        var candidate = new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection, 10),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [" tvdb "] = " 12345 ",
                ["empty"] = "   ",
                [""] = "ignored",
            });

        Assert.Equal("12345", candidate.ProviderIds["Tvdb"]);
        Assert.False(candidate.ProviderIds.ContainsKey("empty"));
    }

    [Fact]
    public void CandidateRejectsReversedMultiEpisodeSpan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection, 10, 20, ArrFileIdentity.Absent),
            seasonNumber: 1,
            episodeNumber: 5,
            episodeNumberEnd: 4));
    }

    [Fact]
    public void SelectorReturnsSingleCandidateMatchingProviderId()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var candidates = new[]
        {
            SeriesCandidate(10, ("Tvdb", "12345")),
            SeriesCandidate(20, ("Tvdb", "99999")),
        };

        var selection = CandidateSelector.Select(identity, candidates, new[] { new ProviderIdMatchRule("Tvdb") });

        Assert.True(selection.IsUnique);
        Assert.Equal(10, ((SonarrIdentity)selection.UniqueCandidate!.RecordIdentity).SeriesId);
        var evidence = Assert.Single(selection.Evidence);
        Assert.Equal(MediaMatchMethod.ProviderId, evidence.Method);
        Assert.Equal("Tvdb", evidence.EvidenceKey);
        Assert.Equal("12345", evidence.MatchedValue);
        Assert.Equal(2, evidence.CandidateCount);
        Assert.Equal(1, evidence.MatchCount);
    }

    [Fact]
    public void SelectorAppliesRulesAsOrderedIdentityFallbacks()
    {
        var identity = SeriesIdentity(("Imdb", "tt1234567"));
        var candidates = new[] { SeriesCandidate(10, ("Imdb", "tt1234567")) };

        var selection = CandidateSelector.Select(
            identity,
            candidates,
            new CandidateMatchRule[]
            {
                new ProviderIdMatchRule("Tvdb"),
                new ProviderIdMatchRule("Imdb"),
            });

        Assert.True(selection.IsUnique);
        Assert.Equal(2, selection.Evidence.Count);
        Assert.Equal(0, selection.Evidence[0].MatchCount);
        Assert.Equal(1, selection.Evidence[1].MatchCount);
        Assert.Equal("Imdb", selection.DecidingEvidence!.EvidenceKey);
    }

    [Fact]
    public void SelectorReportsAmbiguityWhenMultipleCandidatesMatch()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var candidates = new[]
        {
            SeriesCandidate(10, ("Tvdb", "12345")),
            SeriesCandidate(20, ("Tvdb", "12345")),
        };

        var selection = CandidateSelector.Select(identity, candidates, new[] { new ProviderIdMatchRule("Tvdb") });

        Assert.True(selection.IsAmbiguous);
        Assert.False(selection.IsUnique);
        Assert.Equal(2, selection.SurvivorCount);
        Assert.Null(selection.UniqueCandidate);
    }

    [Fact]
    public void SelectorReportsEmptyWhenNoCandidateMatches()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var candidates = new[] { SeriesCandidate(10, ("Tvdb", "99999")) };

        var selection = CandidateSelector.Select(
            identity,
            candidates,
            new CandidateMatchRule[]
            {
                new ProviderIdMatchRule("Tvdb"),
                new ProviderIdMatchRule("Imdb"),
            });

        Assert.True(selection.IsEmpty);
        Assert.Equal(2, selection.Evidence.Count);
        Assert.All(selection.Evidence, evidence => Assert.Equal(0, evidence.MatchCount));
    }

    [Fact]
    public void SelectorNeverUsesTitleOrYearAsIdentity()
    {
        var identity = new MediaIdentity(
            ItemId,
            MediaItemType.Series,
            providerIds: null,
            title: "Example Series",
            productionYear: 2021);
        var candidate = new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection, 10),
            providerIds: null,
            title: "Example Series",
            productionYear: 2021);

        var selection = CandidateSelector.Select(identity, new[] { candidate }, new[] { new ProviderIdMatchRule("Tvdb") });

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void SelectorWithNoRulesSelectsNothingWithoutEvidence()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var candidates = new[]
        {
            SeriesCandidate(10, ("Tvdb", "12345")),
            SeriesCandidate(20, ("Tvdb", "99999")),
        };

        var selection = CandidateSelector.Select(identity, candidates, Array.Empty<CandidateMatchRule>());

        Assert.True(selection.IsEmpty);
        Assert.Empty(selection.Evidence);
        Assert.Null(selection.DecidingEvidence);
    }

    [Fact]
    public void MatchEvidenceRejectsNoneMethodAndInvalidCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MatchEvidence(MediaMatchMethod.None, "Tvdb", "12345", 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MatchEvidence(MediaMatchMethod.ProviderId, "Tvdb", "12345", -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MatchEvidence(MediaMatchMethod.ProviderId, "Tvdb", "12345", 1, 2));
        Assert.Throws<ArgumentException>(
            () => new MatchEvidence(MediaMatchMethod.ProviderId, "  ", "12345", 1, 1));
    }

    [Fact]
    public void ProviderIdMatchRuleRequiresNonEmptyKey()
    {
        Assert.Throws<ArgumentException>(() => new ProviderIdMatchRule("  "));
    }

    private static MediaIdentity SeriesIdentity(params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(ItemId, MediaItemType.Series, providerIds: ToDictionary(providerIds));
    }

    private static MatchCandidate SeriesCandidate(int seriesId, params (string Key, string Value)[] providerIds)
    {
        return new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection, seriesId),
            ToDictionary(providerIds));
    }

    private static Dictionary<string, string> ToDictionary((string Key, string Value)[] providerIds)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in providerIds)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }
}
