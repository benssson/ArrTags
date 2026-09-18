using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using ArrTags.Secrets;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 2.7 provider failure-matrix checks. The shared read boundary must map
/// authentication failures, unavailable or unreachable services, malformed and
/// oversized responses, optional or drifted fields, cancellation, and bounded
/// retries into safe, redacted outcomes for both Sonarr and Radarr. These tests
/// require no live Jellyfin or Arr instance.
/// </summary>
public class ProviderFailureMatrixTests
{
    private const string RadarrApiKey = "radarr-super-secret";
    private const string SonarrApiKey = "sonarr-super-secret";

    private static readonly ArrProviderKind[] AllProviders =
    {
        ArrProviderKind.Radarr,
        ArrProviderKind.Sonarr,
    };

    public static TheoryData<ArrProviderKind, HttpStatusCode> RejectedCredentialStatuses()
    {
        var data = new TheoryData<ArrProviderKind, HttpStatusCode>();
        foreach (var kind in AllProviders)
        {
            data.Add(kind, HttpStatusCode.Unauthorized);
            data.Add(kind, HttpStatusCode.Forbidden);
        }

        return data;
    }

    public static TheoryData<ArrProviderKind, HttpStatusCode> TransientStatuses()
    {
        var data = new TheoryData<ArrProviderKind, HttpStatusCode>();
        foreach (var kind in AllProviders)
        {
            data.Add(kind, HttpStatusCode.Conflict);
            data.Add(kind, HttpStatusCode.TooManyRequests);
            data.Add(kind, HttpStatusCode.InternalServerError);
            data.Add(kind, HttpStatusCode.BadGateway);
            data.Add(kind, HttpStatusCode.ServiceUnavailable);
        }

        return data;
    }

    public static TheoryData<ArrProviderKind, HttpStatusCode> InvalidRequestStatuses()
    {
        var data = new TheoryData<ArrProviderKind, HttpStatusCode>();
        foreach (var kind in AllProviders)
        {
            data.Add(kind, HttpStatusCode.BadRequest);
            data.Add(kind, (HttpStatusCode)422);
        }

        return data;
    }

    public static TheoryData<ArrProviderKind, string> MalformedBodies()
    {
        var data = new TheoryData<ArrProviderKind, string>();
        foreach (var kind in AllProviders)
        {
            data.Add(kind, "<html><body>login</body></html>");
            data.Add(kind, string.Empty);
            data.Add(kind, "null");
            data.Add(kind, "{}");
            data.Add(kind, "[1,2");
            data.Add(kind, "not json");
        }

        return data;
    }

    public static TheoryData<ArrProviderKind, string, string?> VersionDriftBodies()
    {
        var data = new TheoryData<ArrProviderKind, string, string?>();
        foreach (var kind in AllProviders)
        {
            var name = ProviderName(kind);
            data.Add(kind, $$"""{"appName":"{{name}}","version":"99.0.0"}""", "99.0.0");
            data.Add(kind, $$"""{"appName":"{{name.ToLowerInvariant()}}","version":"5.0.0"}""", "5.0.0");
            data.Add(kind, $$"""{"appName":"{{name}}","version":"5.0.0","futureField":{"x":1},"futureArray":[1,2]}""", "5.0.0");
            data.Add(kind, $$"""{"appName":"{{name}}"}""", null);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RejectedCredentialStatuses))]
    public async Task ReadMapsRejectedCredentialsWithoutRetry(ArrProviderKind kind, HttpStatusCode status)
    {
        var harness = CreateHarness(kind, (_, _) => Status(status), FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.AuthenticationFailed, outcome.Error!.Code);
        Assert.Equal(ArrErrorRetryability.AfterConfiguration, outcome.Error.Retryability);
        Assert.Equal(1, harness.Handler.Attempts);
        Assert.DoesNotContain(ApiKeyFor(kind), outcome.Error.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RejectedCredentialStatuses))]
    public async Task ProbeReportsRejectedCredentialsAsAuthenticationFailed(ArrProviderKind kind, HttpStatusCode status)
    {
        var harness = CreateHarness(kind, (_, _) => Status(status), FastRetry);

        var result = await harness.Probe(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(ArrConnectionHealth.AuthenticationFailed, result.Health);
        Assert.Equal(ArrProviderErrorCode.AuthenticationFailed, result.Error!.Code);
        Assert.Equal(1, harness.Handler.Attempts);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task MissingCredentialFailsClosedWithoutHttpCall(ArrProviderKind kind)
    {
        var harness = CreateHarness(
            kind,
            (_, _) => Json(HttpStatusCode.OK, "[]"),
            resolver: new MissingSecretResolver());

        var probe = await harness.Probe(CancellationToken.None);
        var read = await harness.Read(CancellationToken.None);

        Assert.Equal(ArrConnectionHealth.AuthenticationFailed, probe.Health);
        Assert.Equal(ArrProviderErrorCode.AuthenticationFailed, probe.Error!.Code);
        Assert.Equal(ArrProviderErrorCode.AuthenticationFailed, read.Error!.Code);
        Assert.Equal(ArrErrorRetryability.AfterConfiguration, read.Error.Retryability);
        Assert.Equal(0, harness.Handler.Attempts);
    }

    [Theory]
    [MemberData(nameof(TransientStatuses))]
    public async Task ReadRetriesTransientStatusUntilExhaustedAndMapsToUnavailable(ArrProviderKind kind, HttpStatusCode status)
    {
        var harness = CreateHarness(kind, (_, _) => Status(status), FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, outcome.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Later, outcome.Error.Retryability);
        Assert.Equal(2, harness.Handler.Attempts);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ProbeMapsServerErrorToUnavailable(ArrProviderKind kind)
    {
        var harness = CreateHarness(kind, (_, _) => Status(HttpStatusCode.ServiceUnavailable), NoRetry);

        var result = await harness.Probe(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(ArrConnectionHealth.Unavailable, result.Health);
        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, result.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Later, result.Error.Retryability);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ReadMapsUnreachableServiceToUnavailableAfterRetries(ArrProviderKind kind)
    {
        var harness = CreateHarness(
            kind,
            (_, _) => throw new HttpRequestException("connection refused"),
            FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, outcome.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Later, outcome.Error.Retryability);
        Assert.Equal(2, harness.Handler.Attempts);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ReadMapsTimeoutToUnavailableAfterRetries(ArrProviderKind kind)
    {
        var harness = CreateHarness(
            kind,
            (_, _) => throw new OperationCanceledException("simulated request timeout"),
            FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, outcome.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Later, outcome.Error.Retryability);
        Assert.Equal(2, harness.Handler.Attempts);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ReadMapsMissingEndpointToIncompatibleWithoutRetry(ArrProviderKind kind)
    {
        var harness = CreateHarness(kind, (_, _) => Status(HttpStatusCode.NotFound), FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.ProviderIncompatible, outcome.Error!.Code);
        Assert.Equal(ArrErrorRetryability.AfterConfiguration, outcome.Error.Retryability);
        Assert.Equal(1, harness.Handler.Attempts);
    }

    [Theory]
    [MemberData(nameof(InvalidRequestStatuses))]
    public async Task ReadMapsInvalidRequestToInvalidResponseWithoutRetry(ArrProviderKind kind, HttpStatusCode status)
    {
        var harness = CreateHarness(kind, (_, _) => Status(status), FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, outcome.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Never, outcome.Error.Retryability);
        Assert.Equal(1, harness.Handler.Attempts);
    }

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task ReadMapsMalformedBodyToInvalidResponse(ArrProviderKind kind, string body)
    {
        var harness = CreateHarness(kind, (_, _) => Json(HttpStatusCode.OK, body), FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, outcome.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Never, outcome.Error.Retryability);
        Assert.Equal(1, harness.Handler.Attempts);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ProbeMapsMalformedBodyToInvalidResponse(ArrProviderKind kind)
    {
        var harness = CreateHarness(
            kind,
            (_, _) => Json(HttpStatusCode.OK, "<html><body>login</body></html>"),
            NoRetry);

        var result = await harness.Probe(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, result.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Never, result.Error.Retryability);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ReadRejectsOversizedBodyWithoutContentLength(ArrProviderKind kind)
    {
        var oversized = Encoding.UTF8.GetBytes("[" + new string('x', 200_000) + "]");
        var harness = CreateHarness(
            kind,
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ChunkedContent(oversized) },
            limits =>
            {
                NoRetry(limits);
                limits.ProviderResponseLimitBytes = 64 * 1024;
            });

        var outcome = await harness.Read(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, outcome.Error!.Code);
    }

    [Fact]
    public async Task RadarrReadsTolerateMissingOptionalFields()
    {
        var harness = CreateHarness(ArrProviderKind.Radarr, (_, request) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/v3/movie", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":1}]""");
            }

            if (path.EndsWith("/api/v3/moviefile", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":42,"movieId":7}]""");
            }

            return Json(HttpStatusCode.OK, "[]");
        });

        var client = Assert.IsType<RadarrClient>(harness.Client);

        var movies = await client.GetMoviesAsync(CancellationToken.None);
        var movie = Assert.Single(movies.Value!);
        Assert.Equal(1, movie.Id);
        Assert.Null(movie.Title);
        Assert.Null(movie.Year);
        Assert.Null(movie.TmdbId);
        Assert.Null(movie.HasFile);
        Assert.Null(movie.MovieFileId);
        Assert.Null(movie.MovieFile);

        var files = await client.GetMovieFilesAsync(7, CancellationToken.None);
        var file = Assert.Single(files.Value!);
        Assert.Null(file.Quality);
        Assert.Null(file.MediaInfo);
        Assert.Null(file.CustomFormats);
        Assert.Null(file.QualityCutoffNotMet);
    }

    [Fact]
    public async Task SonarrReadsTolerateMissingOptionalFields()
    {
        var harness = CreateHarness(ArrProviderKind.Sonarr, (_, request) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/v3/series", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":12}]""");
            }

            if (path.EndsWith("/api/v3/episode", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":73,"seriesId":12}]""");
            }

            if (path.EndsWith("/api/v3/episodeFile", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """[{"id":418,"seriesId":12}]""");
            }

            return Json(HttpStatusCode.OK, "[]");
        });

        var client = Assert.IsType<SonarrClient>(harness.Client);

        var series = await client.GetSeriesAsync(CancellationToken.None);
        var show = Assert.Single(series.Value!);
        Assert.Equal(12, show.Id);
        Assert.Null(show.Title);
        Assert.Null(show.TvdbId);

        var episodes = await client.GetEpisodesAsync(12, CancellationToken.None);
        var episode = Assert.Single(episodes.Value!);
        Assert.Null(episode.EpisodeFileId);
        Assert.Null(episode.HasFile);
        Assert.Null(episode.EpisodeFile);

        var files = await client.GetEpisodeFilesAsync(12, CancellationToken.None);
        var file = Assert.Single(files.Value!);
        Assert.Null(file.Quality);
        Assert.Null(file.MediaInfo);
        Assert.Null(file.CustomFormats);
        Assert.Null(file.Languages);
    }

    [Theory]
    [MemberData(nameof(VersionDriftBodies))]
    public async Task ProbeToleratesVersionDriftAndUnknownFields(ArrProviderKind kind, string body, string? expectedVersion)
    {
        var harness = CreateHarness(kind, (_, _) => Json(HttpStatusCode.OK, body), NoRetry);

        var result = await harness.Probe(CancellationToken.None);

        Assert.True(result.IsHealthy);
        Assert.Equal(kind, result.Provider!.Kind);
        Assert.Equal(expectedVersion, result.Provider.ApplicationVersion);
        Assert.Equal(ProviderName(kind), result.Provider.DisplayName);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ProbeRejectsMissingProviderIdentityRegardlessOfVersion(ArrProviderKind kind)
    {
        var harness = CreateHarness(
            kind,
            (_, _) => Json(HttpStatusCode.OK, """{"version":"99.0.0"}"""),
            NoRetry);

        var result = await harness.Probe(CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(ArrConnectionHealth.Incompatible, result.Health);
        Assert.Equal(ArrProviderErrorCode.ProviderIncompatible, result.Error!.Code);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task CancellationDuringRetryBackoffStopsFurtherAttempts(ArrProviderKind kind)
    {
        using var source = new CancellationTokenSource();
        var harness = CreateHarness(kind, (attempt, _) =>
        {
            source.Cancel();
            return attempt == 0 ? Status(HttpStatusCode.ServiceUnavailable) : Json(HttpStatusCode.OK, "[]");
        }, FastRetry);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Read(source.Token));

        Assert.Equal(1, harness.Handler.Attempts);
    }

    [Theory]
    [InlineData(ArrProviderKind.Radarr)]
    [InlineData(ArrProviderKind.Sonarr)]
    public async Task ReadReappliesCredentialToEveryRetryAttempt(ArrProviderKind kind)
    {
        var harness = CreateHarness(
            kind,
            (attempt, _) => attempt == 0
                ? Status(HttpStatusCode.ServiceUnavailable)
                : Json(HttpStatusCode.OK, "[]"),
            FastRetry);

        var outcome = await harness.Read(CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.Error?.Message);
        Assert.Equal(2, harness.Handler.Attempts);
        Assert.All(harness.Handler.Requests, request => Assert.Equal(ApiKeyFor(kind), request.ApiKey));
        Assert.All(
            harness.Handler.Requests,
            request => Assert.DoesNotContain(ApiKeyFor(kind), request.Uri.ToString(), StringComparison.Ordinal));
    }

    private static string ProviderName(ArrProviderKind kind)
    {
        return kind == ArrProviderKind.Sonarr ? "Sonarr" : "Radarr";
    }

    private static string ApiKeyFor(ArrProviderKind kind)
    {
        return kind == ArrProviderKind.Sonarr ? SonarrApiKey : RadarrApiKey;
    }

    private static void FastRetry(OperationalLimits limits)
    {
        limits.TransientRetryCount = 1;
        limits.RetryBackoffInitialSeconds = 1;
        limits.RetryBackoffFactor = 1;
        limits.RetryBackoffMaxSeconds = 1;
    }

    private static void NoRetry(OperationalLimits limits)
    {
        limits.TransientRetryCount = 0;
        limits.RetryBackoffInitialSeconds = 1;
        limits.RetryBackoffFactor = 1;
        limits.RetryBackoffMaxSeconds = 1;
    }

    private static Harness CreateHarness(
        ArrProviderKind kind,
        Func<int, HttpRequestMessage, HttpResponseMessage> responder,
        Action<OperationalLimits>? configureLimits = null,
        IPluginSecretResolver? resolver = null)
    {
        var handler = new ScriptedMessageHandler(responder);
        var configuration = new PluginConfiguration();
        var connectionConfiguration = kind == ArrProviderKind.Sonarr
            ? configuration.Sonarr
            : configuration.Radarr;
        connectionConfiguration.Enabled = true;
        connectionConfiguration.BaseUrl = kind == ArrProviderKind.Sonarr
            ? "https://sonarr.local:8989/sonarr"
            : "https://radarr.local:7878/radarr";
        connectionConfiguration.ApiKey = ApiKeyFor(kind);
        configureLimits?.Invoke(configuration.Limits);

        var service = new ConfigurationSnapshotService(configuration);
        var connection = ArrConnectionCatalog.FromSnapshot(service.Current)
            .Single(candidate => candidate.Provider.Kind == kind);
        var httpFactory = new ArrHttpClientFactory(new StubHttpClientFactory(handler));
        var effectiveResolver = resolver ?? service;

        if (kind == ArrProviderKind.Sonarr)
        {
            var client = new SonarrClient(connection, httpFactory, effectiveResolver, configuration.Limits);
            return new Harness(
                client,
                client.ProbeAsync,
                async cancellationToken =>
                {
                    var result = await client.GetSeriesAsync(cancellationToken).ConfigureAwait(false);
                    return ReadOutcome.From(result.IsSuccess, result.Error, result.Value?.Count);
                },
                handler);
        }

        var radarr = new RadarrClient(connection, httpFactory, effectiveResolver, configuration.Limits);
        return new Harness(
            radarr,
            radarr.ProbeAsync,
            async cancellationToken =>
            {
                var result = await radarr.GetMoviesAsync(cancellationToken).ConfigureAwait(false);
                return ReadOutcome.From(result.IsSuccess, result.Error, result.Value?.Count);
            },
            handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage Status(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode);
    }

    private readonly record struct ReadOutcome(bool IsSuccess, ArrProviderError? Error, int Count)
    {
        public static ReadOutcome From(bool isSuccess, ArrProviderError? error, int? count)
        {
            return new ReadOutcome(isSuccess, error, count ?? 0);
        }
    }

    private sealed class Harness
    {
        public Harness(
            IArrProviderClient client,
            Func<CancellationToken, Task<ArrConnectionProbeResult>> probe,
            Func<CancellationToken, Task<ReadOutcome>> read,
            ScriptedMessageHandler handler)
        {
            Client = client;
            Probe = probe;
            Read = read;
            Handler = handler;
        }

        public IArrProviderClient Client { get; }

        public Func<CancellationToken, Task<ArrConnectionProbeResult>> Probe { get; }

        public Func<CancellationToken, Task<ReadOutcome>> Read { get; }

        public ScriptedMessageHandler Handler { get; }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ApiKey);

    private sealed class ScriptedMessageHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpRequestMessage, HttpResponseMessage> _responder;

        public ScriptedMessageHandler(Func<int, HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int Attempts { get; private set; }

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attempt = Attempts;
            Attempts++;
            var apiKey = request.Headers.TryGetValues("X-Api-Key", out var values)
                ? string.Join(",", values)
                : null;
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, apiKey));

            try
            {
                return Task.FromResult(_responder(attempt, request));
            }
            catch (Exception exception)
            {
                return Task.FromException<HttpResponseMessage>(exception);
            }
        }
    }

    private sealed class ChunkedContent : HttpContent
    {
        private readonly byte[] _bytes;

        public ChunkedContent(byte[] bytes)
        {
            _bytes = bytes;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_bytes, 0, _bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
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

    private sealed class MissingSecretResolver : IPluginSecretResolver
    {
        public bool TryAcquire(SecretReference reference, long configurationVersion, [NotNullWhen(true)] out SecretLease? lease)
        {
            lease = null;
            return false;
        }
    }
}
