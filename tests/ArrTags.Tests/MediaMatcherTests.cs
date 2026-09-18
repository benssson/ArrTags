using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.4 checks for the provider-neutral matching orchestration: the
/// documented movie, series, and episode order, the series-before-episode rule,
/// and bounded unsupported outcomes. These tests require no live Jellyfin or Arr
/// instance.
/// </summary>
public class MediaMatcherTests
{
    private static readonly Guid MovieItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeriesItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EpisodeItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly ArrConnection RadarrConnection = BuildConnection(ArrProviderKind.Radarr);
    private static readonly ArrConnection SonarrConnection = BuildConnection(ArrProviderKind.Sonarr);

    [Fact]
    public void MovieMatchesByTmdbWithConnectionScopedIdentity()
    {
        var identity = MovieIdentity(("Tmdb", "603"), ("Imdb", "tt0133093"));
        var candidate = RadarrCandidate(5, fileId: 9, ("Tmdb", "603"), ("Imdb", "tt0000000"));

        var match = MediaMatcher.Match(identity, RadarrConnection.Provider, RadarrConnection.ConnectionId, new[] { candidate });

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(MediaMatchMethod.ProviderId, match.MatchMethod);
        var record = Assert.IsType<RadarrIdentity>(match.RecordIdentity);
        Assert.Equal(5, record.MovieId);
        Assert.Equal("603", match.MatchedProviderIds["Tmdb"]);
    }

    [Fact]
    public void TmdbRuleDecidesBeforeImdbFallback()
    {
        var identity = MovieIdentity(("Tmdb", "603"), ("Imdb", "tt0133093"));
        var byTmdb = RadarrCandidate(5, fileId: 9, ("Tmdb", "603"), ("Imdb", "tt0000000"));
        var byImdb = RadarrCandidate(6, fileId: 10, ("Tmdb", "111"), ("Imdb", "tt0133093"));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { byImdb, byTmdb });

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(5, Assert.IsType<RadarrIdentity>(match.RecordIdentity).MovieId);
    }

    [Fact]
    public void ImdbFallbackAppliesWhenNoTmdbCandidateMatches()
    {
        var identity = MovieIdentity(("Tmdb", "603"), ("Imdb", "tt0133093"));
        var byImdb = RadarrCandidate(6, fileId: 10, ("Tmdb", "111"), ("Imdb", "tt0133093"));
        var unrelated = RadarrCandidate(7, fileId: 11, ("Tmdb", "999"), ("Imdb", "tt9999999"));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { byImdb, unrelated });

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(MediaMatchMethod.ProviderId, match.MatchMethod);
        Assert.Equal("tt0133093", match.MatchedProviderIds["Imdb"]);
        Assert.Equal(6, Assert.IsType<RadarrIdentity>(match.RecordIdentity).MovieId);
    }

    [Fact]
    public void NoCandidateProducesNotFound()
    {
        var identity = MovieIdentity(("Tmdb", "603"));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { RadarrCandidate(6, fileId: 10, ("Tmdb", "111")) });

        Assert.Equal(MediaMatchStatus.NotFound, match.Status);
        Assert.Null(match.RecordIdentity);
    }

    [Fact]
    public void MultipleCandidatesProduceAmbiguous()
    {
        var identity = MovieIdentity(("Tmdb", "603"));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[]
            {
                RadarrCandidate(5, fileId: 9, ("Tmdb", "603")),
                RadarrCandidate(6, fileId: 10, ("Tmdb", "603")),
            });

        Assert.Equal(MediaMatchStatus.Ambiguous, match.Status);
        Assert.Null(match.RecordIdentity);
    }

    [Fact]
    public void UnsupportedItemProviderCombinationProducesUnsupported()
    {
        var identity = MovieIdentity(("Tmdb", "603"));
        var candidate = SonarrSeriesCandidate(10, ("Tvdb", "12345"));

        var match = MediaMatcher.Match(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            new[] { candidate });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Equal(MediaMatchMethod.None, match.MatchMethod);
        Assert.Null(match.RecordIdentity);
        Assert.False(string.IsNullOrWhiteSpace(match.AmbiguityReason));
    }

    [Fact]
    public void TitleAndYearAloneNeverMatchAtTheOrchestrationBoundary()
    {
        var identity = new MediaIdentity(MovieItemId, MediaItemType.Movie, title: "Example", productionYear: 1999);
        var candidate = new MatchCandidate(
            RadarrConnection.ConnectionId,
            ArrProviderKind.Radarr,
            new RadarrIdentity(RadarrConnection.ConnectionId, 5, ArrFileIdentity.Present(9)),
            title: "Example",
            productionYear: 1999);

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { candidate });

        Assert.Equal(MediaMatchStatus.NotFound, match.Status);
    }

    [Fact]
    public void SeriesFallsBackToTmdbWhenTvdbIsAbsent()
    {
        var identity = SeriesIdentity(("Tmdb", "500"));
        var candidate = SonarrSeriesCandidate(10, ("Tmdb", "500"));

        var match = MediaMatcher.Match(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            new[] { candidate });

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(MediaMatchMethod.ProviderId, match.MatchMethod);
        Assert.Equal("500", match.MatchedProviderIds["Tmdb"]);
        Assert.Equal(10, Assert.IsType<SonarrIdentity>(match.RecordIdentity).SeriesId);
    }

    [Fact]
    public void EpisodeMatchesAfterItsParentSeriesMatches()
    {
        var identity = EpisodeIdentity(series: SeriesIdentity(("Tvdb", "12345")), ("Tvdb", "9001"));
        var seriesCandidates = new[] { SonarrSeriesCandidate(10, ("Tvdb", "12345")) };
        var episodeCandidates = new[] { SonarrEpisodeCandidate(10, 73, ("Tvdb", "9001")) };

        var match = MediaMatcher.MatchEpisode(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            seriesCandidates,
            episodeCandidates);

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(MediaMatchMethod.ProviderId, match.MatchMethod);
        var record = Assert.IsType<SonarrIdentity>(match.RecordIdentity);
        Assert.Equal(10, record.SeriesId);
        Assert.Equal(73, record.EpisodeId);
        Assert.Equal("9001", match.MatchedProviderIds["Tvdb"]);
    }

    [Fact]
    public void EpisodeEvaluationIsScopedToTheMatchedSeries()
    {
        var identity = EpisodeIdentity(series: SeriesIdentity(("Tvdb", "12345")), ("Tvdb", "9001"));
        var seriesCandidates = new[] { SonarrSeriesCandidate(10, ("Tvdb", "12345")) };
        var episodeCandidates = new[]
        {
            SonarrEpisodeCandidate(10, 73, ("Tvdb", "9001")),
            SonarrEpisodeCandidate(20, 88, ("Tvdb", "9001")),
        };

        var match = MediaMatcher.MatchEpisode(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            seriesCandidates,
            episodeCandidates);

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(10, Assert.IsType<SonarrIdentity>(match.RecordIdentity).SeriesId);
    }

    [Fact]
    public void EpisodeWithoutSeriesMatchReturnsTheSeriesFailure()
    {
        var identity = EpisodeIdentity(series: SeriesIdentity(("Tvdb", "12345")), ("Tvdb", "9001"));
        var seriesCandidates = new[] { SonarrSeriesCandidate(10, ("Tvdb", "99999")) };
        var episodeCandidates = new[] { SonarrEpisodeCandidate(10, 73, ("Tvdb", "9001")) };

        var match = MediaMatcher.MatchEpisode(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            seriesCandidates,
            episodeCandidates);

        Assert.Equal(MediaMatchStatus.NotFound, match.Status);
        Assert.Null(match.RecordIdentity);
        Assert.False(string.IsNullOrWhiteSpace(match.AmbiguityReason));
    }

    [Fact]
    public void EpisodeWithoutParentSeriesContextIsUnsupported()
    {
        var identity = EpisodeIdentity(series: null, ("Tvdb", "9001"));

        var match = MediaMatcher.MatchEpisode(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            Array.Empty<MatchCandidate>(),
            new[] { SonarrEpisodeCandidate(10, 73, ("Tvdb", "9001")) });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Null(match.RecordIdentity);
    }

    [Fact]
    public void EpisodeAgainstNonSonarrProviderIsUnsupported()
    {
        var identity = EpisodeIdentity(series: SeriesIdentity(("Tvdb", "12345")), ("Tvdb", "9001"));

        var match = MediaMatcher.MatchEpisode(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            Array.Empty<MatchCandidate>(),
            Array.Empty<MatchCandidate>());

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Null(match.RecordIdentity);
    }

    [Fact]
    public void MatcherRejectsNullInputs()
    {
        var identity = MovieIdentity(("Tmdb", "603"));
        var candidates = Array.Empty<MatchCandidate>();

        Assert.Throws<ArgumentNullException>(
            () => MediaMatcher.Match(null!, RadarrConnection.Provider, RadarrConnection.ConnectionId, candidates));
        Assert.Throws<ArgumentNullException>(
            () => MediaMatcher.Match(identity, null!, RadarrConnection.ConnectionId, candidates));
        Assert.Throws<ArgumentNullException>(
            () => MediaMatcher.Match(identity, RadarrConnection.Provider, null!, candidates));
        Assert.Throws<ArgumentNullException>(
            () => MediaMatcher.Match(identity, RadarrConnection.Provider, RadarrConnection.ConnectionId, null!));
    }

    private static MediaIdentity MovieIdentity(params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(MovieItemId, MediaItemType.Movie, providerIds: ToDictionary(providerIds));
    }

    private static MediaIdentity SeriesIdentity(params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(SeriesItemId, MediaItemType.Series, providerIds: ToDictionary(providerIds));
    }

    private static MediaIdentity EpisodeIdentity(MediaIdentity? series, params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(
            EpisodeItemId,
            MediaItemType.Episode,
            providerIds: ToDictionary(providerIds),
            seriesIdentity: series,
            seasonNumber: 2,
            episodeNumber: 4);
    }

    private static MatchCandidate RadarrCandidate(int movieId, int fileId, params (string Key, string Value)[] providerIds)
    {
        return new MatchCandidate(
            RadarrConnection.ConnectionId,
            ArrProviderKind.Radarr,
            new RadarrIdentity(RadarrConnection.ConnectionId, movieId, ArrFileIdentity.Present(fileId)),
            ToDictionary(providerIds));
    }

    private static MatchCandidate SonarrSeriesCandidate(int seriesId, params (string Key, string Value)[] providerIds)
    {
        return new MatchCandidate(
            SonarrConnection.ConnectionId,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection.ConnectionId, seriesId),
            ToDictionary(providerIds));
    }

    private static MatchCandidate SonarrEpisodeCandidate(int seriesId, int episodeId, params (string Key, string Value)[] providerIds)
    {
        return new MatchCandidate(
            SonarrConnection.ConnectionId,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection.ConnectionId, seriesId, episodeId, ArrFileIdentity.Absent),
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

    private static ArrConnection BuildConnection(ArrProviderKind kind, string apiKey = "test-key")
    {
        var configuration = new PluginConfiguration();
        var connectionConfiguration = kind == ArrProviderKind.Sonarr ? configuration.Sonarr : configuration.Radarr;
        connectionConfiguration.Enabled = true;
        connectionConfiguration.BaseUrl = kind == ArrProviderKind.Sonarr
            ? "http://sonarr.local:8989"
            : "http://radarr.local:7878";
        connectionConfiguration.ApiKey = apiKey;

        return ArrConnectionCatalog.FromSnapshot(PluginConfigurationSnapshot.From(configuration))
            .Single(connection => connection.Provider.Kind == kind);
    }
}
