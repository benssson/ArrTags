using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// ADR-013 provider contract fixtures for task 7.1. The declared Sonarr 3.x-4.x
/// and Radarr 3.x-6.x lines share the pinned <c>/api/v3</c> contract: the probe
/// records the observed version without a numeric gate, a fully populated
/// payload maps to canonical badge metadata, a sparse/optional-missing payload
/// maps to explicit unknowns rather than fabricated defaults, and a missing
/// required identity field fails closed as <see cref="ArrProviderErrorCode.ProviderIncompatible"/>.
/// These fixtures require no live Jellyfin or Arr instance.
/// </summary>
public sealed class ProviderContractFixtureTests
{
    private const string RadarrApiKey = "radarr-contract-secret";
    private const string SonarrApiKey = "sonarr-contract-secret";

    private static readonly DateTimeOffset ObservedAt =
        new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<ArrProviderKind, string> DeclaredProviderLines()
    {
        return new TheoryData<ArrProviderKind, string>
        {
            { ArrProviderKind.Sonarr, "3.0.0" },
            { ArrProviderKind.Sonarr, "4.0.0" },
            { ArrProviderKind.Radarr, "3.0.0" },
            { ArrProviderKind.Radarr, "4.0.0" },
            { ArrProviderKind.Radarr, "5.0.0" },
            { ArrProviderKind.Radarr, "6.4.0" },
        };
    }

    [Theory]
    [MemberData(nameof(DeclaredProviderLines))]
    public async Task ProbeAcceptsEveryDeclaredLineAndRecordsItsVersionWithoutGate(ArrProviderKind kind, string version)
    {
        var body = $$"""{"appName":"{{ProviderName(kind)}}","instanceName":"Contract","version":"{{version}}"}""";
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, body));
        var client = CreateClient(kind, handler);

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.True(result.IsHealthy, result.Error?.Message);
        Assert.Equal(kind, result.Provider!.Kind);
        Assert.Equal(version, result.Provider.ApplicationVersion);
        Assert.Equal(ArrProvider.V3ApiContract, result.Provider.ApiContract);
        Assert.Equal(1, handler.Requests);
        Assert.Contains("/api/v3/system/status", handler.LastRequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DeclaredProviderLines))]
    public async Task MissingRequiredIdentityFailsClosedForEveryDeclaredLine(ArrProviderKind kind, string version)
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, $$"""{"version":"{{version}}"}"""));
        var client = CreateClient(kind, handler);

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(ArrConnectionHealth.Incompatible, result.Health);
        Assert.Equal(ArrProviderErrorCode.ProviderIncompatible, result.Error!.Code);
    }

    [Fact]
    public async Task RadarrFullyPopulatedContractMapsCanonicalMetadata()
    {
        var moviesJson = """
            [
              {
                "id": 7,
                "title": "Example",
                "originalTitle": "Example",
                "year": 1999,
                "tmdbId": 603,
                "imdbId": "tt0133093",
                "qualityProfileId": 99,
                "hasFile": true,
                "movieFileId": 42,
                "movieFile": {"id":42,"quality":{"quality":{"id":7,"name":"Bluray-1080p"}}}
              }
            ]
            """;
        var filesJson = """
            [
              {
                "id": 42,
                "movieId": 7,
                "relativePath": "Example (1999)/Example.mkv",
                "size": 12884901888,
                "releaseGroup": "GROUP",
                "quality": {
                  "quality": {"id":7,"name":"Bluray-1080p","source":"bluray","resolution":1080,"modifier":"none"},
                  "revision": {"version":1,"isRepack":false}
                },
                "customFormats": [{"id":1,"name":"HDR"}],
                "customFormatScore": 100,
                "qualityCutoffNotMet": true,
                "mediaInfo": {
                  "audioCodec": "TrueHD Atmos",
                  "audioChannels": 8.0,
                  "videoCodec": "x265",
                  "videoDynamicRange": "HDR",
                  "videoDynamicRangeType": "HDR10",
                  "width": 1920,
                  "height": 1080,
                  "resolution": "1920x1080"
                }
              }
            ]
            """;
        var (client, connection) = CreateRadarrClient(ContractHandler(moviesJson, filesJson));

        var movies = await client.GetMoviesAsync(CancellationToken.None);
        var files = await client.GetMovieFilesAsync(7, CancellationToken.None);
        Assert.True(movies.IsSuccess, movies.Error?.Message);
        Assert.True(files.IsSuccess, files.Error?.Message);

        var metadata = RadarrMetadataMapper.Map(
            connection,
            movies.Value!.Single(),
            files.Value!.Single(),
            ObservedAt);

        var identity = Assert.IsType<RadarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(7, identity.MovieId);
        Assert.Equal(ArrFilePresence.Present, identity.MovieFileIdentity.Presence);
        Assert.Equal(42, identity.MovieFileIdentity.FileId);

        Assert.Equal("Bluray-1080p", metadata.Quality!.Label);
        Assert.Equal("bluray", metadata.Quality.Source);
        Assert.Equal(1080, metadata.Quality.Resolution);
        Assert.Equal("none", metadata.Quality.Modifier);
        Assert.Equal(7, metadata.Quality.ProviderQualityId);

        Assert.Equal(1920, metadata.Resolution!.Width);
        Assert.Equal(1080, metadata.Resolution.Height);
        Assert.Equal(ArrMetadataOrigin.ProviderMediaInfo, metadata.Resolution.Origin);

        Assert.Equal(ArrDynamicRangeKind.Hdr10, metadata.DynamicRange!.Kind);
        Assert.Equal("HDR10", metadata.DynamicRange.Profile);
        Assert.Null(metadata.DolbyVision);

        Assert.Equal("x265", metadata.VideoCodec);
        Assert.Equal("TrueHD Atmos", metadata.AudioCodec);
        Assert.Equal(8, metadata.AudioChannels);
        Assert.NotNull(metadata.AudioFeatures);
        Assert.Contains(ArrAudioFeature.Atmos, metadata.AudioFeatures!);
        Assert.Equal("bluray", metadata.Source);
        Assert.True(metadata.UpgradePending);
        Assert.Equal(new[] { "HDR" }, metadata.CustomBadges);
    }

    [Fact]
    public async Task RadarrSparseOptionalMissingContractMapsUnknowns()
    {
        var moviesJson = """[{"id":7,"hasFile":true,"movieFileId":42}]""";
        var filesJson = """[{"id":42,"movieId":7}]""";
        var (client, connection) = CreateRadarrClient(ContractHandler(moviesJson, filesJson));

        var movies = await client.GetMoviesAsync(CancellationToken.None);
        var files = await client.GetMovieFilesAsync(7, CancellationToken.None);
        Assert.True(movies.IsSuccess, movies.Error?.Message);
        Assert.True(files.IsSuccess, files.Error?.Message);

        var movie = movies.Value!.Single();
        Assert.Null(movie.Title);
        Assert.Null(movie.TmdbId);
        Assert.Null(movie.MovieFile);

        var metadata = RadarrMetadataMapper.Map(connection, movie, files.Value!.Single(), ObservedAt);

        var identity = Assert.IsType<RadarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(ArrFilePresence.Present, identity.MovieFileIdentity.Presence);
        Assert.Equal(42, identity.MovieFileIdentity.FileId);

        Assert.Null(metadata.Quality);
        Assert.Null(metadata.Resolution);
        Assert.Null(metadata.DynamicRange);
        Assert.Null(metadata.DolbyVision);
        Assert.Null(metadata.VideoCodec);
        Assert.Null(metadata.AudioCodec);
        Assert.Null(metadata.AudioChannels);
        Assert.Null(metadata.AudioFeatures);
        Assert.Null(metadata.Source);
        Assert.Null(metadata.UpgradePending);
        Assert.Empty(metadata.CustomBadges);
    }

    [Fact]
    public async Task RadarrContractWithoutAFileKeepsFileIdentityExplicitlyAbsent()
    {
        var moviesJson = """[{"id":7,"hasFile":false}]""";
        var (client, connection) = CreateRadarrClient(ContractHandler(moviesJson, "[]"));

        var movies = await client.GetMoviesAsync(CancellationToken.None);
        Assert.True(movies.IsSuccess, movies.Error?.Message);

        var metadata = RadarrMetadataMapper.Map(connection, movies.Value!.Single(), null, ObservedAt);

        var identity = Assert.IsType<RadarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(ArrFilePresence.Absent, identity.MovieFileIdentity.Presence);
        Assert.Null(metadata.Quality);
        Assert.Null(metadata.Resolution);
        Assert.Null(metadata.AudioFeatures);
    }

    [Fact]
    public async Task SonarrFullyPopulatedContractMapsCanonicalMetadata()
    {
        var seriesJson = """
            [
              {
                "id": 12,
                "title": "Example",
                "year": 2020,
                "tvdbId": 12345,
                "qualityProfileId": 3,
                "profileName": "HD-1080p"
              }
            ]
            """;
        var episodesJson = """
            [
              {
                "id": 73,
                "seriesId": 12,
                "seasonNumber": 2,
                "episodeNumber": 4,
                "episodeFileId": 418,
                "hasFile": true
              }
            ]
            """;
        var filesJson = """
            [
              {
                "id": 418,
                "seriesId": 12,
                "relativePath": "Season 02/Example - S02E04.mkv",
                "size": 4294967296,
                "releaseGroup": "ExampleGroup",
                "quality": {
                  "quality": {"id":3,"name":"WEBDL-1080p","source":"web","resolution":1080},
                  "revision": {"version":1,"real":0,"isRepack":false}
                },
                "qualityCutoffNotMet": true,
                "customFormats": [{"id":1,"name":"HDR"}],
                "customFormatScore": 50,
                "mediaInfo": {
                  "videoCodec": "h264",
                  "audioCodec": "ac3",
                  "audioChannels": 6.0,
                  "width": 1920,
                  "height": 1080,
                  "videoDynamicRangeType": "HDR10"
                }
              }
            ]
            """;
        var (client, connection) = CreateSonarrClient(ContractHandler(seriesJson, episodesJson, filesJson));

        var series = await client.GetSeriesAsync(CancellationToken.None);
        var episodes = await client.GetEpisodesAsync(12, CancellationToken.None);
        var files = await client.GetEpisodeFilesAsync(12, CancellationToken.None);
        Assert.True(series.IsSuccess, series.Error?.Message);
        Assert.True(episodes.IsSuccess, episodes.Error?.Message);
        Assert.True(files.IsSuccess, files.Error?.Message);

        var episode = episodes.Value!.Single();
        var file = SonarrEpisodeFileResolver.Resolve(episode, files.Value!);
        Assert.NotNull(file);

        var metadata = SonarrMetadataMapper.Map(
            connection,
            series.Value!.Single(),
            episode,
            file,
            ObservedAt);

        var identity = Assert.IsType<SonarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(12, identity.SeriesId);
        Assert.Equal(73, identity.EpisodeId);
        Assert.Equal(ArrFilePresence.Present, identity.EpisodeFileIdentity!.Presence);
        Assert.Equal(418, identity.EpisodeFileIdentity!.FileId);

        Assert.Equal("WEBDL-1080p", metadata.Quality!.Label);
        Assert.Equal("web", metadata.Quality.Source);
        Assert.Equal(1080, metadata.Quality.Resolution);
        Assert.Equal(3, metadata.Quality.ProviderQualityId);

        Assert.Equal(1920, metadata.Resolution!.Width);
        Assert.Equal(1080, metadata.Resolution.Height);
        Assert.Equal(ArrMetadataOrigin.ProviderMediaInfo, metadata.Resolution.Origin);

        Assert.Equal(ArrDynamicRangeKind.Hdr10, metadata.DynamicRange!.Kind);
        Assert.Equal("HDR10", metadata.DynamicRange.Profile);

        Assert.Equal("h264", metadata.VideoCodec);
        Assert.Equal("ac3", metadata.AudioCodec);
        Assert.Equal(6, metadata.AudioChannels);
        Assert.NotNull(metadata.AudioFeatures);
        Assert.Empty(metadata.AudioFeatures!);
        Assert.Equal("web", metadata.Source);
        Assert.True(metadata.UpgradePending);
        Assert.Equal(new[] { "HDR" }, metadata.CustomBadges);
    }

    [Fact]
    public async Task SonarrSparseOptionalMissingContractMapsUnknowns()
    {
        var seriesJson = """[{"id":12}]""";
        var episodesJson = """[{"id":73,"seriesId":12,"episodeFileId":418,"hasFile":true,"episodeFile":{"id":418,"seriesId":12}}]""";
        var filesJson = """[{"id":418,"seriesId":12}]""";
        var (client, connection) = CreateSonarrClient(ContractHandler(seriesJson, episodesJson, filesJson));

        var series = await client.GetSeriesAsync(CancellationToken.None);
        var episodes = await client.GetEpisodesAsync(12, CancellationToken.None);
        var files = await client.GetEpisodeFilesAsync(12, CancellationToken.None);
        Assert.True(series.IsSuccess, series.Error?.Message);
        Assert.True(episodes.IsSuccess, episodes.Error?.Message);
        Assert.True(files.IsSuccess, files.Error?.Message);

        var seriesResource = series.Value!.Single();
        Assert.Null(seriesResource.Title);
        Assert.Null(seriesResource.TvdbId);

        var episode = episodes.Value!.Single();
        var file = SonarrEpisodeFileResolver.Resolve(episode, files.Value!);
        Assert.NotNull(file);

        var metadata = SonarrMetadataMapper.Map(connection, seriesResource, episode, file, ObservedAt);

        var identity = Assert.IsType<SonarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(ArrFilePresence.Present, identity.EpisodeFileIdentity!.Presence);
        Assert.Equal(418, identity.EpisodeFileIdentity!.FileId);

        Assert.Null(metadata.Quality);
        Assert.Null(metadata.Resolution);
        Assert.Null(metadata.DynamicRange);
        Assert.Null(metadata.DolbyVision);
        Assert.Null(metadata.VideoCodec);
        Assert.Null(metadata.AudioCodec);
        Assert.Null(metadata.AudioChannels);
        Assert.Null(metadata.AudioFeatures);
        Assert.Null(metadata.Source);
        Assert.Null(metadata.UpgradePending);
        Assert.Empty(metadata.CustomBadges);
    }

    [Fact]
    public async Task SonarrContractWithoutAFileKeepsFileIdentityExplicitlyAbsent()
    {
        var seriesJson = """[{"id":12}]""";
        var episodesJson = """[{"id":73,"seriesId":12,"hasFile":false}]""";
        var (client, connection) = CreateSonarrClient(ContractHandler(seriesJson, episodesJson, "[]"));

        var series = await client.GetSeriesAsync(CancellationToken.None);
        var episodes = await client.GetEpisodesAsync(12, CancellationToken.None);
        Assert.True(series.IsSuccess, series.Error?.Message);
        Assert.True(episodes.IsSuccess, episodes.Error?.Message);

        var episode = episodes.Value!.Single();
        var file = SonarrEpisodeFileResolver.Resolve(episode, Array.Empty<SonarrEpisodeFileResource>());
        Assert.Null(file);

        var metadata = SonarrMetadataMapper.Map(connection, series.Value!.Single(), episode, file, ObservedAt);

        var identity = Assert.IsType<SonarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(ArrFilePresence.Absent, identity.EpisodeFileIdentity!.Presence);
        Assert.Null(metadata.Quality);
        Assert.Null(metadata.Resolution);
        Assert.Null(metadata.AudioFeatures);
    }

    private static string ProviderName(ArrProviderKind kind)
    {
        return kind == ArrProviderKind.Sonarr ? "Sonarr" : "Radarr";
    }

    private static IArrProviderClient CreateClient(ArrProviderKind kind, HttpMessageHandler handler)
    {
        return kind == ArrProviderKind.Sonarr
            ? CreateSonarrClient(handler).Client
            : CreateRadarrClient(handler).Client;
    }

    private static ScriptedHandler ContractHandler(
        string movieOrSeriesJson,
        string movieFileOrEpisodesJson,
        string? filesJson = null)
    {
        return new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/v3/movie", StringComparison.Ordinal)
                || path.EndsWith("/api/v3/series", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, movieOrSeriesJson);
            }

            if (path.EndsWith("/api/v3/episode", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, movieFileOrEpisodesJson);
            }

            if (path.EndsWith("/api/v3/moviefile", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, movieFileOrEpisodesJson);
            }

            if (path.EndsWith("/api/v3/episodeFile", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, filesJson ?? "[]");
            }

            return Json(HttpStatusCode.OK, "[]");
        });
    }

    private static (RadarrClient Client, ArrConnection Connection) CreateRadarrClient(HttpMessageHandler handler)
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = "https://radarr.local:7878/radarr";
        configuration.Radarr.ApiKey = RadarrApiKey;

        var service = new ConfigurationSnapshotService(configuration);
        var connection = ArrConnectionCatalog.FromSnapshot(service.Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Radarr);
        var httpFactory = new ArrHttpClientFactory(new StubHttpClientFactory(handler));

        return (new RadarrClient(connection, httpFactory, service, configuration.Limits), connection);
    }

    private static (SonarrClient Client, ArrConnection Connection) CreateSonarrClient(HttpMessageHandler handler)
    {
        var configuration = new PluginConfiguration();
        configuration.Sonarr.Enabled = true;
        configuration.Sonarr.BaseUrl = "https://sonarr.local:8989/sonarr";
        configuration.Sonarr.ApiKey = SonarrApiKey;

        var service = new ConfigurationSnapshotService(configuration);
        var connection = ArrConnectionCatalog.FromSnapshot(service.Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Sonarr);
        var httpFactory = new ArrHttpClientFactory(new StubHttpClientFactory(handler));

        return (new SonarrClient(connection, httpFactory, service, configuration.Limits), connection);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int Requests { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}
