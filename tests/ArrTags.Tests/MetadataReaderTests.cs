using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using ArrTags.Reconciliation;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 6.3 provider reconciliation readers: candidate
/// assembly, the documented matching order, the validated current-file
/// selection, and canonical metadata mapping through fake read clients. No live
/// Arr instance is required.
/// </summary>
public sealed class MetadataReaderTests
{
    private static readonly ArrConnection RadarrConnection = BuildConnection(ArrProviderKind.Radarr);
    private static readonly ArrConnection SonarrConnection = BuildConnection(ArrProviderKind.Sonarr);

    [Fact]
    public async Task RadarrReaderMatchesAMovieAndMapsItsCurrentFile()
    {
        var identity = MovieIdentity(603);
        var client = new FakeRadarrReadClient(RadarrConnection)
        {
            Movies = ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(new[]
            {
                new RadarrMovieResource { Id = 7, Title = "Example", TmdbId = 603, MovieFileId = 42 },
            }),
            Files = ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(new[]
            {
                new RadarrMovieFileResource
                {
                    Id = 42,
                    MovieId = 7,
                    QualityCutoffNotMet = true,
                    Quality = new RadarrQualityModel
                    {
                        Quality = new RadarrQuality { Id = 7, Name = "Bluray-1080p", Source = "bluray", Resolution = 1080 },
                    },
                    MediaInfo = new RadarrMediaInfoResource { VideoCodec = "h264", Width = 1920, Height = 1080 },
                },
            }),
        };
        var reader = new RadarrMetadataReader(new FakeReadClientFactory { Radarr = client });

        var result = await reader.ReadAsync(identity, RadarrConnection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Match);
        Assert.Equal(MediaMatchStatus.Matched, result.Match!.Status);
        var record = Assert.IsType<RadarrIdentity>(result.Match.RecordIdentity);
        Assert.Equal(7, record.MovieId);
        Assert.Equal(42, record.MovieFileIdentity.FileId);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Bluray-1080p", result.Metadata!.Quality!.Label);
        Assert.Equal("h264", result.Metadata.VideoCodec);
        Assert.True(result.Metadata.UpgradePending);
    }

    [Fact]
    public async Task RadarrReaderReturnsANonMatchedOutcomeWithoutMetadata()
    {
        var identity = MovieIdentity(603);
        var client = new FakeRadarrReadClient(RadarrConnection)
        {
            Movies = ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(new[]
            {
                new RadarrMovieResource { Id = 7, TmdbId = 999, MovieFileId = 42 },
            }),
        };
        var reader = new RadarrMetadataReader(new FakeReadClientFactory { Radarr = client });

        var result = await reader.ReadAsync(identity, RadarrConnection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaMatchStatus.NotFound, result.Match!.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task RadarrReaderPropagatesAFileReadFailure()
    {
        var identity = MovieIdentity(603);
        var client = new FakeRadarrReadClient(RadarrConnection)
        {
            Movies = ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(new[]
            {
                new RadarrMovieResource { Id = 7, TmdbId = 603, MovieFileId = 42 },
            }),
            Files = ArrProviderResults.Failure<IReadOnlyList<RadarrMovieFileResource>>(new ArrProviderError(
                ArrProviderErrorCode.ProviderUnavailable,
                ArrErrorRetryability.Later,
                "unavailable")),
        };
        var reader = new RadarrMetadataReader(new FakeReadClientFactory { Radarr = client });

        var result = await reader.ReadAsync(identity, RadarrConnection, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrErrorRetryability.Later, result.Error!.Retryability);
    }

    [Fact]
    public async Task SonarrReaderMatchesAnEpisodeAndMapsItsCurrentFile()
    {
        var identity = EpisodeIdentity(seriesTvdbId: 12345, episodeTvdbId: 9001);
        var client = new FakeSonarrReadClient(SonarrConnection)
        {
            Series = ArrProviderResults.Success<IReadOnlyList<SonarrSeriesResource>>(new[]
            {
                new SonarrSeriesResource { Id = 12, Title = "Example", TvdbId = 12345 },
            }),
            Episodes = ArrProviderResults.Success<IReadOnlyList<SonarrEpisodeResource>>(new[]
            {
                new SonarrEpisodeResource
                {
                    Id = 73,
                    SeriesId = 12,
                    TvdbId = 9001,
                    SeasonNumber = 2,
                    EpisodeNumber = 4,
                    EpisodeFileId = 418,
                    HasFile = true,
                    EpisodeFile = new SonarrEpisodeFileResource
                    {
                        Id = 418,
                        SeriesId = 12,
                        QualityCutoffNotMet = true,
                        Quality = new SonarrQualityModel
                        {
                            Quality = new SonarrQuality { Id = 3, Name = "WEBDL-1080p", Source = "webdl", Resolution = 1080 },
                        },
                        MediaInfo = new SonarrMediaInfoResource { AudioCodec = "EAC3", VideoCodec = "h264" },
                    },
                },
            }),
        };
        var reader = new SonarrMetadataReader(new FakeReadClientFactory { Sonarr = client });

        var result = await reader.ReadAsync(identity, SonarrConnection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var record = Assert.IsType<SonarrIdentity>(result.Match!.RecordIdentity);
        Assert.Equal(12, record.SeriesId);
        Assert.Equal(73, record.EpisodeId);
        Assert.Equal(418, record.EpisodeFileIdentity!.FileId);
        Assert.NotNull(result.Metadata);
        Assert.Equal("WEBDL-1080p", result.Metadata!.Quality!.Label);
        Assert.Equal("h264", result.Metadata.VideoCodec);
    }

    [Fact]
    public async Task SonarrReaderDoesNotMatchAnEpisodeWhenTheParentSeriesDoesNotMatch()
    {
        var identity = EpisodeIdentity(seriesTvdbId: 12345, episodeTvdbId: 9001);
        var client = new FakeSonarrReadClient(SonarrConnection)
        {
            Series = ArrProviderResults.Success<IReadOnlyList<SonarrSeriesResource>>(new[]
            {
                new SonarrSeriesResource { Id = 12, TvdbId = 999 },
            }),
        };
        var reader = new SonarrMetadataReader(new FakeReadClientFactory { Sonarr = client });

        var result = await reader.ReadAsync(identity, SonarrConnection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaMatchStatus.NotFound, result.Match!.Status);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task SonarrReaderWithoutParentSeriesContextReturnsUnsupported()
    {
        var identity = new MediaIdentity(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            MediaItemType.Episode,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "9001" });
        var reader = new SonarrMetadataReader(new FakeReadClientFactory());

        var result = await reader.ReadAsync(identity, SonarrConnection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaMatchStatus.Unsupported, result.Match!.Status);
        Assert.Null(result.Metadata);
    }

    private static MediaIdentity MovieIdentity(int tmdbId)
    {
        return new MediaIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            MediaItemType.Movie,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tmdb"] = tmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
    }

    private static MediaIdentity EpisodeIdentity(int seriesTvdbId, int episodeTvdbId)
    {
        var series = new MediaIdentity(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            MediaItemType.Series,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tvdb"] = seriesTvdbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        return new MediaIdentity(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            MediaItemType.Episode,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tvdb"] = episodeTvdbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            seriesIdentity: series,
            seasonNumber: 2,
            episodeNumber: 4);
    }

    private static ArrConnection BuildConnection(ArrProviderKind kind)
    {
        var configuration = new PluginConfiguration();
        var connectionConfiguration = kind == ArrProviderKind.Sonarr ? configuration.Sonarr : configuration.Radarr;
        connectionConfiguration.Enabled = true;
        connectionConfiguration.BaseUrl = kind == ArrProviderKind.Sonarr
            ? "http://sonarr.local:8989"
            : "http://radarr.local:7878";
        connectionConfiguration.ApiKey = "test-key";

        return ArrConnectionCatalog.FromSnapshot(PluginConfigurationSnapshot.From(configuration))
            .Single(connection => connection.Provider.Kind == kind);
    }

    private sealed class FakeReadClientFactory : IArrReadClientFactory
    {
        public IRadarrReadClient? Radarr { get; set; }

        public ISonarrReadClient? Sonarr { get; set; }

        public IRadarrReadClient CreateRadarr(ArrConnection connection)
        {
            return Radarr ?? throw new InvalidOperationException("No fake Radarr client was configured.");
        }

        public ISonarrReadClient CreateSonarr(ArrConnection connection)
        {
            return Sonarr ?? throw new InvalidOperationException("No fake Sonarr client was configured.");
        }
    }

    private sealed class FakeRadarrReadClient : IRadarrReadClient
    {
        public FakeRadarrReadClient(ArrConnection connection)
        {
            Connection = connection;
        }

        public ArrProviderKind Kind => ArrProviderKind.Radarr;

        public ArrConnection Connection { get; }

        public ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>> Movies { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(Array.Empty<RadarrMovieResource>());

        public ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>> Files { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(Array.Empty<RadarrMovieFileResource>());

        public Task<ArrConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Movies);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(int movieId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Files);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(
            IReadOnlyList<int> movieIds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Files);
        }
    }

    private sealed class FakeSonarrReadClient : ISonarrReadClient
    {
        public FakeSonarrReadClient(ArrConnection connection)
        {
            Connection = connection;
        }

        public ArrProviderKind Kind => ArrProviderKind.Sonarr;

        public ArrConnection Connection { get; }

        public ArrProviderReadResult<IReadOnlyList<SonarrSeriesResource>> Series { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<SonarrSeriesResource>>(Array.Empty<SonarrSeriesResource>());

        public ArrProviderReadResult<IReadOnlyList<SonarrEpisodeResource>> Episodes { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<SonarrEpisodeResource>>(Array.Empty<SonarrEpisodeResource>());

        public ArrProviderReadResult<IReadOnlyList<SonarrEpisodeFileResource>> Files { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<SonarrEpisodeFileResource>>(Array.Empty<SonarrEpisodeFileResource>());

        public Task<ArrConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ArrProviderReadResult<IReadOnlyList<SonarrSeriesResource>>> GetSeriesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Series);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<SonarrEpisodeResource>>> GetEpisodesAsync(int seriesId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Episodes);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<SonarrEpisodeFileResource>>> GetEpisodeFilesAsync(int seriesId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Files);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<SonarrEpisodeFileResource>>> GetEpisodeFilesAsync(
            IReadOnlyList<int> episodeFileIds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Files);
        }
    }
}
