using System;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.4 checks for provider-specific candidate assembly. The factories map
/// validated provider DTOs into canonical connection-scoped
/// <see cref="MatchCandidate"/> values without leaking provider resources. These
/// tests require no live Jellyfin or Arr instance.
/// </summary>
public class MatchCandidateFactoryTests
{
    private static readonly ArrConnection RadarrConnection = BuildConnection(ArrProviderKind.Radarr);
    private static readonly ArrConnection SonarrConnection = BuildConnection(ArrProviderKind.Sonarr);

    [Fact]
    public void RadarrFactoryMapsIdentityProvidersAndDescriptiveContext()
    {
        var movie = new RadarrMovieResource
        {
            Id = 7,
            Title = "Example Movie",
            Year = 1999,
            TmdbId = 603,
            ImdbId = "tt0133093",
            MovieFileId = 42,
        };

        var candidate = RadarrMatchCandidateFactory.FromMovie(RadarrConnection, movie);

        Assert.Equal(RadarrConnection.ConnectionId, candidate.ConnectionId);
        Assert.Equal(ArrProviderKind.Radarr, candidate.ProviderKind);
        Assert.Equal("603", candidate.ProviderIds[MatchProviderIdKeys.Tmdb]);
        Assert.Equal("tt0133093", candidate.ProviderIds[MatchProviderIdKeys.Imdb]);
        Assert.Equal("Example Movie", candidate.Title);
        Assert.Equal(1999, candidate.ProductionYear);
        var record = Assert.IsType<RadarrIdentity>(candidate.RecordIdentity);
        Assert.Equal(7, record.MovieId);
        Assert.Equal(42, record.MovieFileIdentity.FileId);
    }

    [Fact]
    public void RadarrFactoryOmitsMissingProvidersAndKeepsAbsentFile()
    {
        var movie = new RadarrMovieResource
        {
            Id = 7,
            Title = "Example Movie",
            TmdbId = 0,
            ImdbId = "  ",
            MovieFileId = 0,
        };

        var candidate = RadarrMatchCandidateFactory.FromMovie(RadarrConnection, movie);

        Assert.Empty(candidate.ProviderIds);
        Assert.Equal(ArrFilePresence.Absent, Assert.IsType<RadarrIdentity>(candidate.RecordIdentity).MovieFileIdentity.Presence);
    }

    [Fact]
    public void RadarrFactoryRejectsWrongConnectionAndInvalidIdentifier()
    {
        var movie = new RadarrMovieResource { Id = 7 };

        Assert.Throws<ArgumentException>(() => RadarrMatchCandidateFactory.FromMovie(SonarrConnection, movie));
        Assert.Throws<ArgumentException>(() => RadarrMatchCandidateFactory.FromMovie(RadarrConnection, new RadarrMovieResource { Id = 0 }));
        Assert.Throws<ArgumentNullException>(() => RadarrMatchCandidateFactory.FromMovie(null!, movie));
        Assert.Throws<ArgumentNullException>(() => RadarrMatchCandidateFactory.FromMovie(RadarrConnection, null!));
    }

    [Fact]
    public void SonarrSeriesFactoryMapsIdentityProvidersAndDescriptiveContext()
    {
        var series = new SonarrSeriesResource
        {
            Id = 12,
            Title = "Example Series",
            Year = 2021,
            Path = "/tv/Example",
            TvdbId = 1234567,
            TmdbId = 500,
            ImdbId = "tt1234567",
        };

        var candidate = SonarrMatchCandidateFactory.FromSeries(SonarrConnection, series);

        Assert.Equal(SonarrConnection.ConnectionId, candidate.ConnectionId);
        Assert.Equal(ArrProviderKind.Sonarr, candidate.ProviderKind);
        Assert.Equal("1234567", candidate.ProviderIds[MatchProviderIdKeys.Tvdb]);
        Assert.Equal("500", candidate.ProviderIds[MatchProviderIdKeys.Tmdb]);
        Assert.Equal("tt1234567", candidate.ProviderIds[MatchProviderIdKeys.Imdb]);
        Assert.Equal("Example Series", candidate.Title);
        Assert.Equal(2021, candidate.ProductionYear);
        Assert.Equal("/tv/Example", candidate.PrimaryPath);
        Assert.Equal(12, Assert.IsType<SonarrIdentity>(candidate.RecordIdentity).SeriesId);
    }

    [Fact]
    public void SonarrSeriesFactoryRejectsWrongConnection()
    {
        Assert.Throws<ArgumentException>(
            () => SonarrMatchCandidateFactory.FromSeries(RadarrConnection, new SonarrSeriesResource { Id = 12 }));
    }

    [Fact]
    public void SonarrEpisodeFactoryMapsEpisodeIdentityAndNumbering()
    {
        var series = new SonarrSeriesResource { Id = 12, Title = "Example Series" };
        var episode = new SonarrEpisodeResource
        {
            Id = 73,
            SeriesId = 12,
            TvdbId = 9001,
            Title = "Pilot",
            SeasonNumber = 2,
            EpisodeNumber = 4,
            EpisodeFileId = 418,
            HasFile = true,
        };

        var candidate = SonarrMatchCandidateFactory.FromEpisode(SonarrConnection, series, episode);

        Assert.Equal("9001", candidate.ProviderIds[MatchProviderIdKeys.Tvdb]);
        Assert.Equal("Pilot", candidate.Title);
        Assert.Equal(2, candidate.SeasonNumber);
        Assert.Equal(4, candidate.EpisodeNumber);
        var record = Assert.IsType<SonarrIdentity>(candidate.RecordIdentity);
        Assert.Equal(12, record.SeriesId);
        Assert.Equal(73, record.EpisodeId);
        Assert.Equal(418, record.EpisodeFileIdentity!.FileId);
    }

    [Fact]
    public void SonarrEpisodeFactoryRejectsEpisodeFromAnotherSeries()
    {
        var series = new SonarrSeriesResource { Id = 12 };
        var episode = new SonarrEpisodeResource { Id = 73, SeriesId = 99 };

        Assert.Throws<ArgumentException>(
            () => SonarrMatchCandidateFactory.FromEpisode(SonarrConnection, series, episode));
    }

    [Fact]
    public void SonarrEpisodeFactoryRejectsWrongConnection()
    {
        var series = new SonarrSeriesResource { Id = 12 };
        var episode = new SonarrEpisodeResource { Id = 73, SeriesId = 12 };

        Assert.Throws<ArgumentException>(
            () => SonarrMatchCandidateFactory.FromEpisode(RadarrConnection, series, episode));
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
