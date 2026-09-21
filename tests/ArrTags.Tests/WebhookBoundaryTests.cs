using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Secrets;
using ArrTags.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused tests for the task 6.7 inbound webhook boundary: constant-time
/// authentication through the ADR-005 webhook lease, bounded payload handling,
/// tolerant bounded parsing, replay/duplicate coalescing, bounded rate behavior,
/// safe status codes, and the Jellyfin plugin controller registration contract.
/// They require no live Jellyfin or Arr instance.
/// </summary>
public sealed class WebhookBoundaryTests
{
    private const string Secret = "correct-horse-battery-staple";

    [Fact]
    public async Task ValidSecretIsAuthenticatedThroughTheWebhookLeaseAndAccepted()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(Secret);
        harness.SetBody(RadarrDownload());

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(1, harness.Intake.Count);
        Assert.Equal(SecretReference.WebhookAuthentication, harness.Resolver.LastReference);
        Assert.Equal(harness.Configuration.Current.ConfigurationVersion, harness.Resolver.LastVersion);
        Assert.Equal(1, harness.Resolver.AcquireCalls);
    }

    [Fact]
    public async Task MissingSecretHeaderFailsClosedWithoutWork()
    {
        using var harness = new WebhookHarness();
        harness.SetBody(RadarrDownload());

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, harness.Intake.Count);
        Assert.Equal(0, harness.Resolver.AcquireCalls);
        Assert.Empty(harness.ResponseBody());
    }

    [Fact]
    public async Task WrongSecretFailsClosedWithoutWork()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret("wrong-secret");
        harness.SetBody(RadarrDownload());

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, harness.Intake.Count);
        Assert.Equal(1, harness.Resolver.AcquireCalls);
    }

    [Fact]
    public async Task NearMissSecretFailsClosed()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(Secret + "x");
        harness.SetBody(RadarrDownload());

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task OversizedSecretCandidateFailsClosedWithoutAllocating()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(new string('a', WebhookAuthentication.MaxCandidateLength + 1));
        harness.SetBody(RadarrDownload());

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, harness.Resolver.AcquireCalls);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task AbsentSecretConfigurationFailsClosed()
    {
        using var harness = new WebhookHarness(webhookSecret: null);
        harness.SetSecret(Secret);
        harness.SetBody(RadarrDownload());

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, harness.Resolver.AcquireCalls);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task AuthenticationFailureNeverLeaksTheSecretOrPayload()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret("wrong-secret");
        harness.SetBody(RadarrDownload());

        await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.Empty(harness.ResponseBody());
        foreach (var header in harness.Context.Response.Headers)
        {
            Assert.DoesNotContain(Secret, header.Value.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("wrong-secret", header.Value.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DeclaredOversizedBodyIsRejectedWithPayloadTooLarge()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(Secret);
        harness.SetBody(RadarrDownload());
        harness.SetContentLength(harness.Configuration.Current.Limits.WebhookMaxPayloadBytes + 1);

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task StreamedOversizedBodyIsRejectedWithPayloadTooLarge()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(Secret);
        var oversized = new string('a', (int)harness.Configuration.Current.Limits.WebhookMaxPayloadBytes + 1);
        harness.SetBody(oversized);
        harness.SetContentLength(null);

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"eventType\":\"Download\"")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"foo\":1}")]
    [InlineData("{\"eventType\":42}")]
    [InlineData("{\"eventType\":\"Download\"}")]
    [InlineData("{\"eventType\":\"Download\",\"movie\":{}}")]
    [InlineData("{\"eventType\":\"Download\",\"movie\":{\"id\":0}}")]
    public async Task EmptyMalformedTruncatedAndWrongShapedPayloadsAreRejectedWithBadRequest(string body)
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(Secret);
        harness.SetBody(body);

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<BadRequestResult>(result);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task UnsupportedEventTypeIsAcknowledgedWithoutWork()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(Secret);
        harness.SetBody("{\"eventType\":\"Test\",\"instanceName\":\"Radarr\"}");

        var result = await harness.PostAsync(ArrProviderKind.Radarr);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(0, harness.Intake.Count);
    }

    [Fact]
    public async Task DuplicateAndReplayedDeliveriesCoalesceInTheIntake()
    {
        using var harness = new WebhookHarness();
        harness.SetSecret(Secret);
        harness.SetBody(RadarrDownload());

        Assert.IsType<AcceptedResult>(await harness.PostAsync(ArrProviderKind.Radarr));
        Assert.IsType<AcceptedResult>(await harness.PostAsync(ArrProviderKind.Radarr));
        Assert.IsType<AcceptedResult>(await harness.PostAsync(ArrProviderKind.Radarr));

        Assert.Equal(1, harness.Intake.Count);
    }

    [Fact]
    public void IntakeOverflowDropsWithoutThrowing()
    {
        using var intake = new WebhookIntake(1);

        Assert.True(intake.TrySubmit(RadarrEvent(movieId: 1, eventType: WebhookEventType.Download)));
        Assert.False(intake.TrySubmit(RadarrEvent(movieId: 2, eventType: WebhookEventType.Download)));
        Assert.Equal(1, intake.Capacity);
        Assert.Equal(1, intake.Count);
    }

    [Fact]
    public void StoppedIntakeRejectsNewEvents()
    {
        using var intake = new WebhookIntake(4);
        intake.StopAccepting();

        Assert.False(intake.TrySubmit(RadarrEvent(movieId: 1, eventType: WebhookEventType.Download)));
        Assert.Equal(0, intake.Count);
    }

    [Fact]
    public void CoalescingWindowIsBoundedAndEvictsTheOldestKey()
    {
        var window = new WebhookCoalescingWindow(TimeSpan.FromMinutes(1), capacity: 2);
        var now = DateTimeOffset.UtcNow;

        Assert.True(window.TryAdmit(Key(1), now));
        Assert.True(window.TryAdmit(Key(2), now));
        Assert.False(window.TryAdmit(Key(1), now));

        // A third distinct key evicts the oldest live key rather than growing.
        Assert.True(window.TryAdmit(Key(3), now));
        Assert.Equal(2, window.Count);
        Assert.True(window.TryAdmit(Key(1), now));
        Assert.Equal(2, window.Count);
    }

    [Fact]
    public void CoalescingWindowExpiresOldKeys()
    {
        var window = new WebhookCoalescingWindow(TimeSpan.FromSeconds(5), capacity: 8);
        var now = DateTimeOffset.UtcNow;

        Assert.True(window.TryAdmit(Key(1), now));
        Assert.False(window.TryAdmit(Key(1), now.AddSeconds(4)));
        Assert.True(window.TryAdmit(Key(1), now.AddSeconds(6)));
    }

    [Fact]
    public void SonarrDownloadPayloadMapsBoundedRecordAndFileHints()
    {
        var payload = Encoding.UTF8.GetBytes(SonarrDownload());

        Assert.True(WebhookEventParser.TryParse(payload, ArrProviderKind.Sonarr, out var webhookEvent, out var error));

        Assert.Equal(WebhookParseError.None, error);
        Assert.NotNull(webhookEvent);
        Assert.Equal(ArrProviderKind.Sonarr, webhookEvent!.ProviderKind);
        Assert.Equal(WebhookEventType.Download, webhookEvent.EventType);
        Assert.True(webhookEvent.IsUpgrade);
        Assert.Equal(7, webhookEvent.SeriesId);
        Assert.Equal(new[] { 101, 102 }, webhookEvent.EpisodeIds);
        Assert.Equal(555, webhookEvent.EpisodeFileId);
    }

    [Fact]
    public void RadarrDownloadPayloadMapsBoundedRecordAndFileHints()
    {
        var payload = Encoding.UTF8.GetBytes(RadarrDownload());

        Assert.True(WebhookEventParser.TryParse(payload, ArrProviderKind.Radarr, out var webhookEvent, out _));

        Assert.NotNull(webhookEvent);
        Assert.Equal(WebhookEventType.Download, webhookEvent!.EventType);
        Assert.True(webhookEvent.IsUpgrade);
        Assert.Equal(123, webhookEvent.MovieId);
        Assert.Equal(456, webhookEvent.MovieFileId);
        Assert.Null(webhookEvent.SeriesId);
    }

    [Fact]
    public void UnknownFieldsAndPropertyCaseAreTolerated()
    {
        const string payload = "{\"EventType\":\"Download\",\"extra\":{\"a\":[1,2,3]},\"Movie\":{\"Id\":9},\"movieFile\":{\"id\":10}}";

        Assert.True(WebhookEventParser.TryParse(
            Encoding.UTF8.GetBytes(payload),
            ArrProviderKind.Radarr,
            out var webhookEvent,
            out _));

        Assert.NotNull(webhookEvent);
        Assert.Equal(9, webhookEvent!.MovieId);
        Assert.Equal(10, webhookEvent.MovieFileId);
    }

    [Fact]
    public void EpisodeEntriesAreBounded()
    {
        var episodes = string.Join(',', Enumerable.Range(1, WebhookEventParser.MaxEpisodeEntries + 10)
            .Select(id => FormattableString.Invariant($"{{\"id\":{id},\"seriesId\":7}}")));
        var payload = Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{{\"eventType\":\"Download\",\"series\":{{\"id\":7}},\"episodes\":[{episodes}]}}"));

        Assert.True(WebhookEventParser.TryParse(payload, ArrProviderKind.Sonarr, out var webhookEvent, out _));

        Assert.NotNull(webhookEvent);
        Assert.Equal(WebhookEventParser.MaxEpisodeEntries, webhookEvent!.EpisodeIds.Count);
    }

    [Fact]
    public void SeriesLevelEventMapsTheSeriesHintOnly()
    {
        const string payload = "{\"eventType\":\"SeriesDelete\",\"series\":{\"id\":7}}";

        Assert.True(WebhookEventParser.TryParse(
            Encoding.UTF8.GetBytes(payload),
            ArrProviderKind.Sonarr,
            out var webhookEvent,
            out _));

        Assert.NotNull(webhookEvent);
        Assert.Equal(WebhookEventType.Deleted, webhookEvent!.EventType);
        Assert.Equal(7, webhookEvent.SeriesId);
        Assert.Empty(webhookEvent.EpisodeIds);
    }

    [Fact]
    public void ControllerIsAnExportedAnonymousControllerBaseWithFixedRoutes()
    {
        var type = typeof(ArrTagsWebhookController);

        Assert.True(type.IsPublic);
        Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
        Assert.NotNull(type.GetCustomAttributes(typeof(ApiControllerAttribute), inherit: false).SingleOrDefault());
        Assert.NotNull(type.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false).SingleOrDefault());
        Assert.Equal(
            ArrTagsWebhookController.RoutePrefix,
            type.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>().Single().Template);

        var sonarr = type.GetMethod(nameof(ArrTagsWebhookController.ReceiveSonarrAsync))!;
        var radarr = type.GetMethod(nameof(ArrTagsWebhookController.ReceiveRadarrAsync))!;
        Assert.Equal("Sonarr", sonarr.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Cast<HttpPostAttribute>().Single().Template);
        Assert.Equal("Radarr", radarr.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Cast<HttpPostAttribute>().Single().Template);
    }

    [Fact]
    public void WebhookBoundaryHasNoProviderWriteRendererOrPublisherDependencies()
    {
        var parameterTypes = typeof(ArrTagsWebhookController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        foreach (var parameterType in parameterTypes)
        {
            var ns = parameterType.Namespace ?? string.Empty;
            Assert.DoesNotContain("ArrTags.Providers", ns, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrTags.Artwork", ns, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrTags.Rendering", ns, StringComparison.Ordinal);
        }

        Assert.Contains(typeof(IWebhookIntake), parameterTypes);
    }

    [Fact]
    public void WebhookComponentsHaveNoProviderWritePublicationOrRendererDependencies()
    {
        var forbidden = new[]
        {
            typeof(ArrTags.Artwork.IArtworkImageWriter),
            typeof(ArrTags.Artwork.ArtworkPublisher),
            typeof(ArrTags.Rendering.IRenderer),
            typeof(ArrTags.Providers.IArrProviderClient),
            typeof(ArrTags.Providers.Radarr.RadarrClient),
            typeof(ArrTags.Providers.Sonarr.SonarrClient),
            typeof(ArrTags.Reconciliation.MetadataReconciliationProcessor),
        };

        var webhookTypes = typeof(ArrTagsWebhookController).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "ArrTags.Webhooks" && type.IsPublic)
            .ToArray();

        Assert.NotEmpty(webhookTypes);
        foreach (var type in webhookTypes)
        {
            foreach (var constructor in type.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    Assert.DoesNotContain(parameter.ParameterType, forbidden);
                }
            }
        }
    }

    [Fact]
    public void OperationalLimitsValidateTheWebhookPayloadBound()
    {
        var limits = new OperationalLimits { WebhookMaxPayloadBytes = 1024 };
        var errors = new List<string>();
        limits.Validate(errors);
        Assert.Contains(errors, error => error.Contains("WebhookMaxPayloadBytes", StringComparison.Ordinal));

        var valid = new OperationalLimits();
        var validErrors = new List<string>();
        valid.Validate(validErrors);
        Assert.DoesNotContain(validErrors, error => error.Contains("WebhookMaxPayloadBytes", StringComparison.Ordinal));
        Assert.Equal(OperationalLimits.DefaultWebhookMaxPayloadBytes, valid.WebhookMaxPayloadBytes);
    }

    [Fact]
    public void RegistratorRegistersTheBoundedWebhookIntake()
    {
        var services = new ServiceCollection();

        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var intake = provider.GetRequiredService<IWebhookIntake>();

        Assert.IsType<WebhookIntake>(intake);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(WebhookReconciliationResolver));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(WebhookIntakeService));
    }

    private static string RadarrDownload()
    {
        return "{\"eventType\":\"Download\",\"instanceName\":\"Radarr\",\"applicationUrl\":\"\","
            + "\"movie\":{\"id\":123,\"title\":\"Example\",\"year\":2020,\"tmdbId\":603,\"imdbId\":\"tt0133093\"},"
            + "\"remoteMovie\":{\"tmdbId\":603,\"imdbId\":\"tt0133093\",\"title\":\"Example\",\"year\":2020},"
            + "\"movieFile\":{\"id\":456,\"relativePath\":\"Example.mkv\",\"path\":\"/movies/Example.mkv\"},"
            + "\"isUpgrade\":true,\"downloadClient\":\"qBittorrent\","
            + "\"unknownField\":{\"nested\":[1,2,3]}}";
    }

    private static string SonarrDownload()
    {
        return "{\"eventType\":\"Download\",\"instanceName\":\"Sonarr\",\"applicationUrl\":\"\","
            + "\"series\":{\"id\":7,\"title\":\"Example\",\"tvdbId\":12345},"
            + "\"episodes\":[{\"id\":101,\"seriesId\":7,\"seasonNumber\":2,\"episodeNumber\":5},{\"id\":102,\"seriesId\":7}],"
            + "\"episodeFile\":{\"id\":555,\"relativePath\":\"Example.S02E05.mkv\"},"
            + "\"isUpgrade\":true}";
    }

    private static WebhookEvent RadarrEvent(int movieId, WebhookEventType eventType)
    {
        return new WebhookEvent(ArrProviderKind.Radarr, eventType, movieId: movieId);
    }

    private static WebhookCoalesceKey Key(int movieId)
    {
        return new WebhookCoalesceKey(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: movieId);
    }

    private sealed class RecordingSecretResolver : IPluginSecretResolver
    {
        private readonly IPluginSecretResolver _inner;

        public RecordingSecretResolver(IPluginSecretResolver inner)
        {
            _inner = inner;
        }

        public int AcquireCalls { get; private set; }

        public SecretReference? LastReference { get; private set; }

        public long LastVersion { get; private set; }

        public bool TryAcquire(SecretReference reference, long configurationVersion, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SecretLease? lease)
        {
            AcquireCalls++;
            LastReference = reference;
            LastVersion = configurationVersion;
            return _inner.TryAcquire(reference, configurationVersion, out lease);
        }
    }

    private sealed class WebhookHarness : IDisposable
    {
        public WebhookHarness(string? webhookSecret = Secret)
        {
            var configuration = new PluginConfiguration
            {
                WebhookSecret = webhookSecret ?? string.Empty,
                Sonarr = new ArrConnectionConfiguration
                {
                    Enabled = true,
                    BaseUrl = "http://sonarr.test",
                    ApiKey = "sonarr-key",
                },
                Radarr = new ArrConnectionConfiguration
                {
                    Enabled = true,
                    BaseUrl = "http://radarr.test",
                    ApiKey = "radarr-key",
                },
            };

            Configuration = new ConfigurationSnapshotService(configuration);
            Resolver = new RecordingSecretResolver(Configuration);
            Intake = new WebhookIntake(8);
            Context = new DefaultHttpContext();
            Context.Request.Body = new MemoryStream();
            Controller = new ArrTagsWebhookController(Configuration, Resolver, Intake)
            {
                ControllerContext = new ControllerContext { HttpContext = Context },
            };
        }

        public ConfigurationSnapshotService Configuration { get; }

        public RecordingSecretResolver Resolver { get; }

        public WebhookIntake Intake { get; }

        public DefaultHttpContext Context { get; }

        public ArrTagsWebhookController Controller { get; }

        private byte[] _body = Array.Empty<byte>();
        private long? _contentLengthOverride;
        private bool _hasContentLengthOverride;

        public void SetSecret(string secret)
        {
            Context.Request.Headers[ArrTagsWebhookController.SecretHeaderName] = secret;
        }

        public void SetBody(string body)
        {
            _body = Encoding.UTF8.GetBytes(body);
            Context.Request.Body = new MemoryStream(_body);
            Context.Request.ContentLength = _body.Length;
        }

        public void SetContentLength(long? value)
        {
            _contentLengthOverride = value;
            _hasContentLengthOverride = true;
        }

        public Task<IActionResult> PostAsync(ArrProviderKind providerKind)
        {
            // Each delivery is an independent request body; reset the stream so
            // repeated deliveries observe the same payload.
            Context.Request.Body = new MemoryStream(_body);
            Context.Request.ContentLength = _hasContentLengthOverride
                ? _contentLengthOverride
                : _body.Length;
            return Controller.ReceiveAsync(providerKind, CancellationToken.None);
        }

        public string ResponseBody()
        {
            Context.Response.Body.Position = 0;
            using var reader = new StreamReader(Context.Response.Body, Encoding.UTF8, leaveOpen: true);
            return reader.ReadToEnd();
        }

        public void Dispose()
        {
            Intake.Dispose();
        }
    }
}
