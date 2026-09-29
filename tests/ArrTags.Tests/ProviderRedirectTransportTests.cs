using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// SEC-21.5-01 transport checks: a provider origin that answers a <c>3xx</c>
/// must not cause the plugin to issue a request to the <c>Location</c> origin
/// (let alone one carrying <c>X-Api-Key</c>), all four named provider clients
/// must be registered with redirect-following disabled including the two
/// opt-in insecure-TLS clients, and a not-followed <c>3xx</c> must surface as a
/// bounded provider error without changing the configured retry bound. The
/// tests stand up two loopback origins and use the real
/// <see cref="ArrTagsServiceRegistrator"/> registration and the real provider
/// clients; they require no live Jellyfin, Sonarr, or Radarr instance.
/// </summary>
public class ProviderRedirectTransportTests
{
    private const string ApiKey = "sec-21-5-01-transport-secret";
    private const string BodySentinel = "REDIRECT-RESPONSE-BODY-SENTINEL";
    private const string HeaderSentinel = "REDIRECT-RESPONSE-HEADER-SENTINEL";

    /// <summary>
    /// The bounded period the redirect test observes the second origin after the
    /// probe completes. A followed redirect would have issued the second request
    /// before <c>SendAsync</c> returned; the period covers the origin's recorder
    /// scheduling.
    /// </summary>
    private static readonly TimeSpan RedirectObservationPeriod = TimeSpan.FromMilliseconds(250);

    [Theory]
    [InlineData(ArrProviderKind.Sonarr, false)]
    [InlineData(ArrProviderKind.Sonarr, true)]
    [InlineData(ArrProviderKind.Radarr, false)]
    [InlineData(ArrProviderKind.Radarr, true)]
    public async Task ConfiguredProviderOriginReceivesTheApiKeyHeader(
        ArrProviderKind kind,
        bool allowInsecureTls)
    {
        await using var provider = RecordingHttpOrigin.Start(_ => JsonSystemStatusResponse(kind));

        using var services = BuildServices();
        var (client, _) = BuildClient(services, kind, provider.BaseUri, allowInsecureTls, limits => limits.TransientRetryCount = 2);

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.True(result.IsHealthy, result.Error?.Message);
        var request = Assert.Single(provider.Requests);
        Assert.StartsWith("GET /api/v3/system/status HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("X-Api-Key: " + ApiKey, request, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, request[..request.IndexOf("\r\n", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ArrProviderKind.Sonarr, false)]
    [InlineData(ArrProviderKind.Sonarr, true)]
    [InlineData(ArrProviderKind.Radarr, false)]
    [InlineData(ArrProviderKind.Radarr, true)]
    public async Task ProviderRedirectIsNotFollowedAndTheKeyNeverReachesTheTarget(
        ArrProviderKind kind,
        bool allowInsecureTls)
    {
        await using var target = RecordingHttpOrigin.Start(_ => JsonSystemStatusResponse(kind));
        await using var provider = RecordingHttpOrigin.Start(_ => RedirectResponse(new Uri(target.BaseUri, "collect")));

        using var services = BuildServices();
        var (client, _) = BuildClient(services, kind, provider.BaseUri, allowInsecureTls, limits => limits.TransientRetryCount = 2);

        var result = await client.ProbeAsync(CancellationToken.None);
        await Task.Delay(RedirectObservationPeriod);

        // The configured origin received exactly one key-bearing request and
        // answered it with a 3xx; the not-followed redirect is terminal.
        var request = Assert.Single(provider.Requests);
        Assert.Contains("X-Api-Key: " + ApiKey, request, StringComparison.Ordinal);

        // The Location origin received nothing at all, so the key cannot have
        // been re-sent across the hop.
        Assert.Empty(target.Requests);

        // The 3xx surfaces as the bounded provider error, carrying no secret,
        // response body, response header value, or Location value.
        Assert.False(result.IsHealthy);
        Assert.NotNull(result.Error);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, result.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Never, result.Error.Retryability);
        var message = result.Error.Message ?? string.Empty;
        Assert.True(message.Length <= ArrProviderError.MaxMessageLength);
        Assert.Contains("redirect", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiKey, message, StringComparison.Ordinal);
        Assert.DoesNotContain(BodySentinel, message, StringComparison.Ordinal);
        Assert.DoesNotContain(HeaderSentinel, message, StringComparison.Ordinal);
        Assert.DoesNotContain(target.BaseUri.ToString(), message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllFourNamedProviderClientsForbidRedirectsAndKeepTheirTlsPolicy()
    {
        using var services = BuildServices();
        var options = services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();

        foreach (var kind in new[] { ArrProviderKind.Sonarr, ArrProviderKind.Radarr })
        {
            foreach (var policy in new[] { ArrTlsPolicy.Strict, ArrTlsPolicy.AllowInsecure })
            {
                var name = ArrHttpClientNames.For(kind, policy);
                var clientOptions = options.Get(name);

                // A registration that drops the handler configuration leaves the
                // action list empty and the default redirect-following handler in
                // place, which fails both assertions below.
                Assert.NotEmpty(clientOptions.HttpMessageHandlerBuilderActions);
                var builder = new CapturingHandlerBuilder();
                foreach (var action in clientOptions.HttpMessageHandlerBuilderActions)
                {
                    action(builder);
                }

                Assert.False(FollowsRedirects(builder.CapturedPrimaryHandler), $"{name} must not follow redirects.");
                if (policy == ArrTlsPolicy.AllowInsecure)
                {
                    Assert.True(
                        AcceptsAnyServerCertificate(builder.CapturedPrimaryHandler),
                        $"{name} must keep the opt-in TLS relaxation.");
                }
                else
                {
                    Assert.False(
                        AcceptsAnyServerCertificate(builder.CapturedPrimaryHandler),
                        $"{name} must keep strict server-certificate validation.");
                }
            }
        }
    }

    [Fact]
    public async Task TransientServerErrorStillUsesTheConfiguredRetryBound()
    {
        await using var provider = RecordingHttpOrigin.Start(_ => PlainResponse(503, "Service Unavailable", "{}"));

        using var services = BuildServices();
        var (client, _) = BuildClient(services, ArrProviderKind.Sonarr, provider.BaseUri, allowInsecureTls: false, limits => limits.TransientRetryCount = 1);

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, result.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Later, result.Error.Retryability);
        Assert.Equal(2, provider.Requests.Count);
        Assert.All(provider.Requests, request => Assert.Contains("X-Api-Key: " + ApiKey, request, StringComparison.Ordinal));
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);
        return services.BuildServiceProvider();
    }

    private static (IArrProviderClient Client, ArrConnection Connection) BuildClient(
        IServiceProvider services,
        ArrProviderKind kind,
        Uri providerBaseUri,
        bool allowInsecureTls,
        Action<OperationalLimits>? configureLimits)
    {
        var configuration = new PluginConfiguration();
        var connectionConfiguration = kind == ArrProviderKind.Sonarr ? configuration.Sonarr : configuration.Radarr;
        connectionConfiguration.Enabled = true;
        connectionConfiguration.BaseUrl = providerBaseUri.GetLeftPart(UriPartial.Authority);
        connectionConfiguration.ApiKey = ApiKey;
        connectionConfiguration.AllowInsecureTls = allowInsecureTls;
        configureLimits?.Invoke(configuration.Limits);

        var snapshotService = new ConfigurationSnapshotService(configuration);
        var connection = ArrConnectionCatalog.FromSnapshot(snapshotService.Current)
            .Single(candidate => candidate.Provider.Kind == kind);
        Assert.True(connection.Enabled);
        Assert.True(connection.HasApiKey);
        Assert.Equal(
            allowInsecureTls ? ArrTlsPolicy.AllowInsecure : ArrTlsPolicy.Strict,
            connection.TlsPolicy);

        var httpClientFactory = services.GetRequiredService<IArrHttpClientFactory>();
        IArrProviderClient client = kind == ArrProviderKind.Sonarr
            ? new SonarrClient(connection, httpClientFactory, snapshotService, snapshotService.Current.Limits)
            : new RadarrClient(connection, httpClientFactory, snapshotService, snapshotService.Current.Limits);
        return (client, connection);
    }

    private static string JsonSystemStatusResponse(ArrProviderKind kind)
    {
        var appName = kind == ArrProviderKind.Sonarr ? "Sonarr" : "Radarr";
        return PlainResponse(200, "OK", "{\"appName\":\"" + appName + "\",\"instanceName\":\"Transport test\",\"version\":\"4.0.0\"}");
    }

    private static string RedirectResponse(Uri location)
    {
        return PlainResponse(
            302,
            "Found",
            BodySentinel,
            ("Location", location.ToString()),
            ("X-Provider-Debug", HeaderSentinel));
    }

    private static string PlainResponse(int statusCode, string reasonPhrase, string body, params (string Name, string Value)[] headers)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reasonPhrase).Append("\r\n");
        foreach (var (name, value) in headers)
        {
            builder.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        builder
            .Append("Content-Type: application/json\r\n")
            .Append("Content-Length: ")
            .Append(Encoding.UTF8.GetByteCount(body))
            .Append("\r\n")
            .Append("Connection: close\r\n\r\n")
            .Append(body);
        return builder.ToString();
    }

    private static bool FollowsRedirects(HttpMessageHandler? handler)
    {
        return handler switch
        {
            HttpClientHandler httpClientHandler => httpClientHandler.AllowAutoRedirect,
            SocketsHttpHandler socketsHttpHandler => socketsHttpHandler.AllowAutoRedirect,
            // No explicit primary handler means the factory's default handler
            // applies, which follows redirects.
            null => true,
            _ => throw new InvalidOperationException($"Unrecognized primary handler type {handler.GetType()}."),
        };
    }

    private static bool AcceptsAnyServerCertificate(HttpMessageHandler? handler)
    {
        return handler switch
        {
            HttpClientHandler httpClientHandler => httpClientHandler.ServerCertificateCustomValidationCallback is not null,
            SocketsHttpHandler socketsHttpHandler => socketsHttpHandler.SslOptions.RemoteCertificateValidationCallback is not null,
            _ => false,
        };
    }

    private sealed class CapturingHandlerBuilder : HttpMessageHandlerBuilder
    {
        private HttpMessageHandler? _primaryHandler;

        public override string Name { get; set; } = "provider-redirect-transport-test";

        public override HttpMessageHandler PrimaryHandler
        {
            get => _primaryHandler ?? throw new InvalidOperationException("No primary handler was configured.");
            set => _primaryHandler = value;
        }

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();

        public HttpMessageHandler? CapturedPrimaryHandler => _primaryHandler;

        public override HttpMessageHandler Build() => PrimaryHandler;
    }

    /// <summary>
    /// A minimal loopback HTTP/1.1 origin: it records the header block of every
    /// received request and answers with a canned response. It binds a dynamic
    /// loopback port so concurrently running test classes cannot collide.
    /// </summary>
    private sealed class RecordingHttpOrigin : IAsyncDisposable
    {
        private const int MaximumHeaderBytes = 64 * 1024;

        private readonly TcpListener _listener;
        private readonly Func<string, string> _responder;
        private readonly ConcurrentQueue<string> _requests = new ConcurrentQueue<string>();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly Task _acceptLoop;

        private RecordingHttpOrigin(TcpListener listener, Func<string, string> responder)
        {
            _listener = listener;
            _responder = responder;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _acceptLoop = AcceptLoopAsync();
        }

        public int Port { get; }

        public Uri BaseUri => new Uri($"http://127.0.0.1:{Port}/", UriKind.Absolute);

        public IReadOnlyList<string> Requests => _requests.ToArray();

        public static RecordingHttpOrigin Start(Func<string, string> responder)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(backlog: 8);
            return new RecordingHttpOrigin(listener, responder);
        }

        public async ValueTask DisposeAsync()
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The listener was stopped while the accept loop was pending.
            }

            _shutdown.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = HandleAsync(connection);
            }
        }

        private async Task HandleAsync(TcpClient connection)
        {
            try
            {
                using (connection)
                {
                    var stream = connection.GetStream();
                    var request = await ReadHeaderBlockAsync(stream).ConfigureAwait(false);
                    _requests.Enqueue(request);
                    var response = Encoding.UTF8.GetBytes(_responder(request));
                    await stream.WriteAsync(response).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task<string> ReadHeaderBlockAsync(NetworkStream stream)
        {
            var received = new MemoryStream();
            var buffer = new byte[4096];
            while (received.Length < MaximumHeaderBytes)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await received.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                var text = Encoding.ASCII.GetString(received.ToArray());
                if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(received.ToArray());
        }
    }
}
