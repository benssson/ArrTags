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
using ArrTags.Providers.Radarr;
using ArrTags.Secrets;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 2.3 checks for the Radarr v3 read boundary: authenticated probing,
/// local movie reads, the dedicated movie-file read, bounded failure mapping,
/// response-size limits, redaction, and cancellation. These tests require no
/// live Jellyfin or Radarr instance.
/// </summary>
public class RadarrClientTests
{
    private const string ApiKey = "radarr-super-secret";

    [Fact]
    public async Task ProbeReadsSystemStatusAndReturnsProvider()
    {
        var (client, handler) = CreateClient(
            _ => Json(HttpStatusCode.OK, """{"appName":"Radarr","instanceName":"Main","version":"5.3.0"}"""));

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.True(result.IsHealthy);
        Assert.Equal(ArrProviderKind.Radarr, client.Kind);
        Assert.Equal("Main", result.Provider!.DisplayName);
        Assert.Equal("5.3.0", result.Provider.ApplicationVersion);
        Assert.Contains("movie", result.Provider.Capabilities);
        Assert.Contains("movieFile", result.Provider.Capabilities);
        Assert.Equal(
            "https://radarr.local:7878/radarr/api/v3/system/status",
            handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task ProbeSendsApiKeyHeaderAndNotUrl()
    {
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, """{"appName":"Radarr"}"""));

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
    public async Task ProbeRejectsNonRadarrIdentity()
    {
        var (client, _) = CreateClient(_ => Json(HttpStatusCode.OK, """{"appName":"Sonarr"}"""));

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
    public async Task GetMoviesParsesLocalMovies()
    {
        var json = """
            [
              {
                "id": 1,
                "title": "One",
                "tmdbId": 11,
                "imdbId": "tt1",
                "hasFile": true,
                "movieFileId": 5,
                "movieFile": {"id":5,"quality":{"quality":{"name":"WEBDL-1080p"}}}
              },
              {"id":2,"title":"Two","hasFile":false}
            ]
            """;
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var result = await client.GetMoviesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("One", result.Value[0].Title);
        Assert.Equal(5, result.Value[0].MovieFileId);
        Assert.Equal("WEBDL-1080p", result.Value[0].MovieFile!.Quality!.Quality!.Name);
        Assert.False(result.Value[1].HasFile);
        Assert.Equal("https://radarr.local:7878/radarr/api/v3/movie", handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task GetMovieFilesUsesDedicatedEndpointAndParsesActualQuality()
    {
        var json = """
            [
              {
                "id": 42,
                "movieId": 7,
                "relativePath": "Movie.mkv",
                "size": 123,
                "releaseGroup": "GROUP",
                "quality": {
                  "quality": {"id":7,"name":"Bluray-1080p","source":"bluray","resolution":1080,"modifier":"none"},
                  "revision": {"version":1,"isRepack":false}
                },
                "customFormats": [{"id":1,"name":"HDR"}],
                "customFormatScore": 100,
                "qualityCutoffNotMet": true,
                "mediaInfo": {
                  "audioCodec":"TrueHD Atmos",
                  "audioChannels":8.0,
                  "videoCodec":"x265",
                  "videoDynamicRangeType":"HDR10"
                }
              }
            ]
            """;
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var result = await client.GetMovieFilesAsync(7, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var file = result.Value!.Single();
        Assert.Equal(42, file.Id);
        Assert.Equal("Bluray-1080p", file.Quality!.Quality!.Name);
        Assert.Equal("bluray", file.Quality.Quality.Source);
        Assert.True(file.QualityCutoffNotMet);
        Assert.Equal("TrueHD Atmos", file.MediaInfo!.AudioCodec);
        Assert.Equal("HDR", file.CustomFormats!.Single().Name);
        Assert.Equal(100, file.CustomFormatScore);
        Assert.Equal(
            "https://radarr.local:7878/radarr/api/v3/moviefile?movieId=7",
            handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task GetMovieFilesRejectsNonPositiveMovieId()
    {
        var (client, handler) = CreateClient(_ => Json(HttpStatusCode.OK, "[]"));

        var result = await client.GetMovieFilesAsync(0, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, result.Error!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReadRejectsOversizedResponse()
    {
        var (client, _) = CreateClient(
            _ => Json(HttpStatusCode.OK, new string('x', 200_000)),
            configureLimits: limits => limits.ProviderResponseLimitBytes = 64 * 1024);

        var result = await client.GetMoviesAsync(CancellationToken.None);

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

        var result = await client.GetMoviesAsync(CancellationToken.None);

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

        var result = await client.GetMoviesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ReadHonorsCancellation()
    {
        var client = CreateClient(new CancelOnlyMessageHandler());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMoviesAsync(source.Token));
    }

    [Fact]
    public async Task FailureMessagesDoNotLeakApiKey()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(ApiKey, Encoding.UTF8, "text/plain"),
        }, configureLimits: limits => limits.TransientRetryCount = 0);

        var result = await client.GetMoviesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(ApiKey, result.Error!.Message ?? string.Empty, StringComparison.Ordinal);
    }

    private static (RadarrClient Client, RecordingHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IPluginSecretResolver? resolver = null,
        Action<OperationalLimits>? configureLimits = null)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        return (BuildClient(handler, resolver, configureLimits), handler);
    }

    private static RadarrClient CreateClient(
        HttpMessageHandler handler,
        IPluginSecretResolver? resolver = null,
        Action<OperationalLimits>? configureLimits = null)
    {
        return BuildClient(handler, resolver, configureLimits);
    }

    private static RadarrClient BuildClient(
        HttpMessageHandler handler,
        IPluginSecretResolver? resolver,
        Action<OperationalLimits>? configureLimits)
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = "https://radarr.local:7878/radarr";
        configuration.Radarr.ApiKey = ApiKey;
        configureLimits?.Invoke(configuration.Limits);

        var service = new ConfigurationSnapshotService(configuration);
        var connection = ArrConnectionCatalog.FromSnapshot(service.Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Radarr);
        var httpFactory = new ArrHttpClientFactory(new StubHttpClientFactory(handler));

        return new RadarrClient(connection, httpFactory, resolver ?? service, configuration.Limits);
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
