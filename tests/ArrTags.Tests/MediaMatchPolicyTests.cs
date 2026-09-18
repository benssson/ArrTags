using System;
using System.Collections.Generic;
using System.Globalization;
using ArrTags.Media;
using ArrTags.Matching;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.3 checks for the match status policy: zero surviving candidates become
/// <see cref="MediaMatchStatus.NotFound"/>, multiple survivors become
/// <see cref="MediaMatchStatus.Ambiguous"/>, and only a single survivor is
/// accepted. Title and year never resolve either rejected case. These tests
/// require no live Jellyfin or Arr instance.
/// </summary>
public class MediaMatchPolicyTests
{
    private static readonly Guid ItemId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly ArrConnectionId SonarrConnection =
        ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:8989");
    private static readonly ArrProvider SonarrProvider =
        new(ArrProviderKind.Sonarr, SonarrConnection.Value, "Sonarr");

    [Fact]
    public void ZeroCandidatesResolveToNotFoundWithoutIdentity()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var selection = CandidateSelector.Select(
            identity,
            Array.Empty<MatchCandidate>(),
            new[] { new ProviderIdMatchRule("Tvdb") });

        var match = MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, selection);

        Assert.Equal(MediaMatchStatus.NotFound, match.Status);
        Assert.Equal(MediaMatchMethod.None, match.MatchMethod);
        Assert.Null(match.RecordIdentity);
        Assert.Empty(match.MatchedProviderIds);
        Assert.False(string.IsNullOrWhiteSpace(match.AmbiguityReason));
    }

    [Fact]
    public void NonMatchingCandidatesResolveToNotFound()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var selection = CandidateSelector.Select(
            identity,
            new[] { SeriesCandidate(10, ("Tvdb", "99999")) },
            new[]
            {
                new ProviderIdMatchRule("Tvdb"),
                new ProviderIdMatchRule("Imdb"),
            });

        var match = MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, selection);

        Assert.Equal(MediaMatchStatus.NotFound, match.Status);
        Assert.Null(match.RecordIdentity);
    }

    [Fact]
    public void MultipleSurvivorsResolveToAmbiguousWithoutIdentity()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var selection = CandidateSelector.Select(
            identity,
            new[]
            {
                SeriesCandidate(10, ("Tvdb", "12345")),
                SeriesCandidate(20, ("Tvdb", "12345")),
            },
            new[] { new ProviderIdMatchRule("Tvdb") });

        var match = MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, selection);

        Assert.Equal(MediaMatchStatus.Ambiguous, match.Status);
        Assert.Equal(MediaMatchMethod.None, match.MatchMethod);
        Assert.Null(match.RecordIdentity);
        Assert.Contains("Multiple", match.AmbiguityReason, StringComparison.Ordinal);
    }

    [Fact]
    public void UniqueSurvivorResolvesToMatchedWithEvidence()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var candidate = SeriesCandidate(10, ("Tvdb", "12345"));
        var selection = CandidateSelector.Select(
            identity,
            new[] { candidate, SeriesCandidate(20, ("Tvdb", "99999")) },
            new[] { new ProviderIdMatchRule("Tvdb") });

        var match = MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, selection);

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(MediaMatchMethod.ProviderId, match.MatchMethod);
        Assert.Same(candidate.RecordIdentity, match.RecordIdentity);
        Assert.Equal("12345", match.MatchedProviderIds["Tvdb"]);
        Assert.Null(match.AmbiguityReason);
    }

    [Fact]
    public void TitleAndYearAloneNeverResolveAMatch()
    {
        var identity = new MediaIdentity(
            ItemId,
            MediaItemType.Series,
            title: "Example Series",
            productionYear: 2021);
        var candidate = new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection, 10),
            title: "Example Series",
            productionYear: 2021);
        var selection = CandidateSelector.Select(identity, new[] { candidate }, new[] { new ProviderIdMatchRule("Tvdb") });

        var match = MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, selection);

        Assert.Equal(MediaMatchStatus.NotFound, match.Status);
        Assert.Null(match.RecordIdentity);
    }

    [Fact]
    public void AmbiguityIsNotResolvedByTitleOrYear()
    {
        var identity = new MediaIdentity(
            ItemId,
            MediaItemType.Series,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "12345" },
            title: "Example Series",
            productionYear: 2021);
        var selection = CandidateSelector.Select(
            identity,
            new[]
            {
                SeriesCandidate(10, ("Tvdb", "12345"), ("Title", "Example Series")),
                SeriesCandidate(20, ("Tvdb", "12345"), ("Title", "Example Series")),
            },
            new[] { new ProviderIdMatchRule("Tvdb") });

        var match = MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, selection);

        Assert.Equal(MediaMatchStatus.Ambiguous, match.Status);
        Assert.Null(match.RecordIdentity);
    }

    [Fact]
    public void NumberMatchResolvesWithoutProviderIdentifiers()
    {
        var identity = EpisodeIdentity(2, 3, ("Tvdb", "12345"));
        var candidate = new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection, 10, 20, ArrFileIdentity.Absent),
            seasonNumber: 2,
            episodeNumber: 3);
        var selection = CandidateSelector.Select(identity, new[] { candidate }, new[] { new SeasonEpisodeMatchRule() });

        var match = MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, selection);

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(MediaMatchMethod.Number, match.MatchMethod);
        Assert.Empty(match.MatchedProviderIds);
    }

    [Fact]
    public void ResolveRejectsNullInputs()
    {
        var identity = SeriesIdentity(("Tvdb", "12345"));
        var selection = CandidateSelector.Select(identity, Array.Empty<MatchCandidate>(), Array.Empty<CandidateMatchRule>());

        Assert.Throws<ArgumentNullException>(
            () => MediaMatchPolicy.Resolve(null!, SonarrProvider, SonarrConnection, selection));
        Assert.Throws<ArgumentNullException>(
            () => MediaMatchPolicy.Resolve(identity, null!, SonarrConnection, selection));
        Assert.Throws<ArgumentNullException>(
            () => MediaMatchPolicy.Resolve(identity, SonarrProvider, null!, selection));
        Assert.Throws<ArgumentNullException>(
            () => MediaMatchPolicy.Resolve(identity, SonarrProvider, SonarrConnection, null!));
    }

    private static MediaIdentity SeriesIdentity(params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(ItemId, MediaItemType.Series, providerIds: ToDictionary(providerIds));
    }

    private static MediaIdentity EpisodeIdentity(int season, int episode, params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(
            ItemId,
            MediaItemType.Episode,
            providerIds: ToDictionary(providerIds),
            seasonNumber: season,
            episodeNumber: episode);
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

    private sealed class SeasonEpisodeMatchRule : CandidateMatchRule
    {
        public SeasonEpisodeMatchRule()
            : base(MediaMatchMethod.Number, "SeasonEpisode")
        {
        }

        public override bool IsSatisfiedBy(MediaIdentity identity, MatchCandidate candidate, out string? matchedValue)
        {
            matchedValue = null;
            if (identity.SeasonNumber is not int season
                || identity.EpisodeNumber is not int episode
                || candidate.SeasonNumber != season
                || candidate.EpisodeNumber != episode)
            {
                return false;
            }

            matchedValue = season.ToString(CultureInfo.InvariantCulture)
                + "x"
                + episode.ToString(CultureInfo.InvariantCulture);
            return true;
        }
    }
}
