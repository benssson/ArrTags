using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Providers.Sonarr;
using ArrTags.Secrets;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 2.4 checks for the Sonarr v3 read boundary: authenticated probing,
/// series, episode, and episode-file reads, the validated
/// <c>episodeFileId == episodeFile.id</c> join, bounded failure mapping,
/// response-size limits, redaction, and cancellation. These tests require no
/// live Jellyfin or Sonarr instance.
/// </summary>
public class SonarrClientTests
{
    private const string ApiKey = "sonarr-super-secret";

    [Fact]
    public async Task ProbeReadsSystemStatusAndReturnsProvider()
    {
        var (client, handler) = CreateClient(
            _ => Json(HttpStatusCode.OK, """{"appName":"Sonarr","instanceName":"Main","version":"4.0.0"}"""));

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.True(result.IsHealthy);
        Assert.Equal(ArrProviderKind.Sonarr, client.Kind);
        Assert.Equal("Main", result.Provider!.DisplayName);
        Assert.Equal("4.0.0", result.Provider.ApplicationVersion);
        Assert.Contains("series", result.Provider.Capabilities);
        Assert.Contains("episode", result.Provider.Capabilities);
        Assert.Contains("episodeFile", result.Provider.Capabilities);
        Assert.Equal(
            "https://sonarr.local:8989/sonarr/api/v3/system/status",
            handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task ProbeSendsApiKeyHeaderAndNotUrl()
    {
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, """{"appName":"Sonarr"}"""));

        await client.ProbeAsync(CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal(ApiKey, request.ApiKey);
        Assert.DoesNotContain(ApiKey, request.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeMapsUnauthorizedToAuthenticationFailed()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(ArrConnectionHealth.AuthenticationFailed, result.Health);
        Assert.Equal(ArrProviderErrorCode.AuthenticationFailed, result.Error!.Code);
        Assert.DoesNotContain(ApiKey, result.Error.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeMapsServerErrorToUnavailable()
    {
        var (client, _) = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            configureLimits: limits => limits.TransientRetryCount = 0);

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.Equal(ArrConnectionHealth.Unavailable, result.Health);
        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task ProbeRejectsNonSonarrIdentity()
    {
        var (client, _) = CreateClient(_ => Json(HttpStatusCode.OK, """{"appName":"Radarr"}"""));

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.Equal(ArrConnectionHealth.Incompatible, result.Health);
        Assert.Equal(ArrProviderErrorCode.ProviderIncompatible, result.Error!.Code);
    }

    [Fact]
    public async Task MissingLeaseFailsWithoutAnyHttpCall()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler, new StubSecretResolver());

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(ArrConnectionHealth.AuthenticationFailed, result.Health);
        Assert.Equal(ArrProviderErrorCode.AuthenticationFailed, result.Error!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetSeriesParsesLocalSeries()
    {
        var json = """
            [
              {
                "id": 12,
                "title": "Example",
                "originalTitle": "Example",
                "year": 2020,
                "status": "continuing",
                "path": "/tv/Example",
                "tvdbId": 1234567,
                "tmdbId": 99,
                "tvMazeId": 7,
                "imdbId": "tt123",
                "qualityProfileId": 3,
                "profileName": "HD-1080p",
                "monitored": true
              }
            ]
            """;
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var result = await client.GetSeriesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var series = result.Value!.Single();
        Assert.Equal(12, series.Id);
        Assert.Equal("Example", series.Title);
        Assert.Equal(1234567, series.TvdbId);
        Assert.Equal("HD-1080p", series.ProfileName);
        Assert.True(series.Monitored);
        Assert.Equal("https://sonarr.local:8989/sonarr/api/v3/series", handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task GetEpisodesUsesSeriesSelectorAndParsesEmbeddedFile()
    {
        var json = """
            [
              {
                "id": 73,
                "seriesId": 12,
                "tvdbId": 1234567,
                "seasonNumber": 2,
                "episodeNumber": 4,
                "episodeFileId": 418,
                "hasFile": true,
                "episodeFile": {
                  "id": 418,
                  "seriesId": 12,
                  "quality": {"quality": {"id": 3, "name": "WEBDL-1080p"}}
                }
              },
              {"id": 74, "seriesId": 12, "episodeFileId": 0, "hasFile": false}
            ]
            """;
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var result = await client.GetEpisodesAsync(12, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(418, result.Value[0].EpisodeFileId);
        Assert.Equal("WEBDL-1080p", result.Value[0].EpisodeFile!.Quality!.Quality!.Name);
        Assert.False(result.Value[1].HasFile);
        Assert.Equal(
            "https://sonarr.local:8989/sonarr/api/v3/episode?seriesId=12&includeEpisodeFile=true",
            handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task GetEpisodeFilesParsesActualQualityAndTechnicalInfo()
    {
        var json = """
            [
              {
                "id": 418,
                "seriesId": 12,
                "relativePath": "Season 02/Example - S02E04.mkv",
                "size": 4294967296,
                "releaseGroup": "ExampleGroup",
                "releaseType": "single",
                "quality": {
                  "quality": {"id":3,"name":"WEBDL-1080p","source":"web","resolution":1080},
                  "revision": {"version":1,"real":0,"isRepack":false}
                },
                "qualityCutoffNotMet": true,
                "customFormats": [{"id":1,"name":"HDR"}],
                "customFormatScore": 50,
                "mediaInfo": {
                  "videoCodec":"h264",
                  "audioCodec":"ac3",
                  "width":1920,
                  "height":1080,
                  "videoDynamicRangeType":"HDR10"
                },
                "languages": [{"id":1,"name":"English"}]
              }
            ]
            """;
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var result = await client.GetEpisodeFilesAsync(12, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var file = result.Value!.Single();
        Assert.Equal(418, file.Id);
        Assert.Equal("WEBDL-1080p", file.Quality!.Quality!.Name);
        Assert.Equal("web", file.Quality.Quality.Source);
        Assert.True(file.QualityCutoffNotMet);
        Assert.Equal(1920, file.MediaInfo!.Width);
        Assert.Equal(1080, file.MediaInfo.Height);
        Assert.Equal("HDR10", file.MediaInfo.VideoDynamicRangeType);
        Assert.Equal("HDR", file.CustomFormats!.Single().Name);
        Assert.Equal(50, file.CustomFormatScore);
        Assert.Equal("English", file.Languages!.Single().Name);
        Assert.Equal(
            "https://sonarr.local:8989/sonarr/api/v3/episodeFile?seriesId=12",
            handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task GetEpisodesRejectsNonPositiveSeriesId()
    {
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, "[]"));

        var result = await client.GetEpisodesAsync(0, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, result.Error!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEpisodeFilesRejectsNonPositiveSeriesId()
    {
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, "[]"));

        var result = await client.GetEpisodeFilesAsync(-1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, result.Error!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ResolvePrefersEmbeddedFileWhenIdentifierMatches()
    {
        var embedded = BuildEpisodeFile(418);
        var episode = new SonarrEpisodeResource
        {
            Id = 73,
            SeriesId = 12,
            EpisodeFileId = 418,
            HasFile = true,
            EpisodeFile = embedded,
        };

        var resolved = SonarrEpisodeFileResolver.Resolve(episode, Array.Empty<SonarrEpisodeFileResource>());

        Assert.Same(embedded, resolved);
    }

    [Fact]
    public void ResolveUsesSeriesInventoryWhenEmbeddedMissingOrMismatched()
    {
        var inventory = BuildEpisodeFile(418);
        var episode = new SonarrEpisodeResource
        {
            Id = 73,
            SeriesId = 12,
            EpisodeFileId = 418,
            HasFile = true,
            EpisodeFile = BuildEpisodeFile(999),
        };

        var resolved = SonarrEpisodeFileResolver.Resolve(episode, new[] { inventory });

        Assert.Same(inventory, resolved);
    }

    [Fact]
    public void ResolveReturnsAbsentWhenEpisodeHasNoFile()
    {
        var noIdentifier = new SonarrEpisodeResource { Id = 73, SeriesId = 12, EpisodeFileId = 0, HasFile = false };
        var missingFlag = new SonarrEpisodeResource { Id = 74, SeriesId = 12, EpisodeFileId = 418, HasFile = false };
        var inventory = new[] { BuildEpisodeFile(418) };

        Assert.Null(SonarrEpisodeFileResolver.Resolve(noIdentifier, inventory));
        Assert.Null(SonarrEpisodeFileResolver.Resolve(missingFlag, inventory));
    }

    [Fact]
    public void ResolveReturnsAbsentWhenIdentifierNotFoundInInventory()
    {
        var episode = new SonarrEpisodeResource
        {
            Id = 73,
            SeriesId = 12,
            EpisodeFileId = 500,
            HasFile = true,
            EpisodeFile = BuildEpisodeFile(999),
        };

        Assert.Null(SonarrEpisodeFileResolver.Resolve(episode, new[] { BuildEpisodeFile(418) }));
    }

    [Fact]
    public async Task ReadRejectsOversizedResponse()
    {
        var (client, _) = CreateClient(
            _ => Json(HttpStatusCode.OK, new string('x', 200_000)),
            configureLimits: limits => limits.ProviderResponseLimitBytes = 64 * 1024);

        var result = await client.GetSeriesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, result.Error!.Code);
    }

    [Fact]
    public async Task ReadMapsMalformedResponseToInvalidResponse()
    {
        var (client, _) = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html"),
            });

        var result = await client.GetSeriesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, result.Error!.Code);
    }

    [Fact]
    public async Task ReadRetriesTransientFailureWithinBoundedPolicy()
    {
        var attempts = 0;
        var (client, handler) = CreateClient(
            _ => Interlocked.Increment(ref attempts) == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(HttpStatusCode.OK, "[]"),
            configureLimits: limits =>
            {
                limits.TransientRetryCount = 1;
                limits.RetryBackoffInitialSeconds = 1;
                limits.RetryBackoffFactor = 1;
                limits.RetryBackoffMaxSeconds = 1;
            });

        var result = await client.GetSeriesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ReadHonorsCancellation()
    {
        var client = CreateClient(new CancelOnlyMessageHandler());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetSeriesAsync(source.Token));
    }

    [Fact]
    public async Task FailureMessagesDoNotLeakApiKey()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(ApiKey, Encoding.UTF8, "text/plain"),
        }, configureLimits: limits => limits.TransientRetryCount = 0);

        var result = await client.GetSeriesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(ApiKey, result.Error!.Message ?? string.Empty, StringComparison.Ordinal);
    }

    private static SonarrEpisodeFileResource BuildEpisodeFile(int id)
    {
        return new SonarrEpisodeFileResource { Id = id, SeriesId = 12 };
    }

    private static (SonarrClient Client, RecordingHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IPluginSecretResolver? resolver = null,
        Action<OperationalLimits>? configureLimits = null)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        return (BuildClient(handler, resolver, configureLimits), handler);
    }

    private static SonarrClient CreateClient(
        HttpMessageHandler handler,
        IPluginSecretResolver? resolver = null,
        Action<OperationalLimits>? configureLimits = null)
    {
        return BuildClient(handler, resolver, configureLimits);
    }

    private static SonarrClient BuildClient(
        HttpMessageHandler handler,
        IPluginSecretResolver? resolver,
        Action<OperationalLimits>? configureLimits)
    {
        var configuration = new PluginConfiguration();
        configuration.Sonarr.Enabled = true;
        configuration.Sonarr.BaseUrl = "https://sonarr.local:8989/sonarr";
        configuration.Sonarr.ApiKey = ApiKey;
        configureLimits?.Invoke(configuration.Limits);

        var service = new ConfigurationSnapshotService(configuration);
        var connection = ArrConnectionCatalog.FromSnapshot(service.Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Sonarr);
        var httpFactory = new ArrHttpClientFactory(new StubHttpClientFactory(handler));

        return new SonarrClient(connection, httpFactory, resolver ?? service, configuration.Limits);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ApiKey);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var apiKey = request.Headers.TryGetValues("X-Api-Key", out var values)
                ? string.Join(",", values)
                : null;

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, apiKey));
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class CancelOnlyMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The cancellation test handler never completes.");
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

    private sealed class StubSecretResolver : IPluginSecretResolver
    {
        public bool TryAcquire(SecretReference reference, long configurationVersion, [NotNullWhen(true)] out SecretLease? lease)
        {
            lease = null;
            return false;
        }
    }
}
