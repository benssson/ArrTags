using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Concurrency;
using ArrTags.Configuration;
using ArrTags.Logging;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.Secrets;
using ArrTags.State;
using ArrTags.Updates;
using ArrTags.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 10 task 10.2 coverage for the bounded, redacted log call sites
/// (ADR-020 clauses 4 and 6). Every instrumented boundary is driven with
/// sentinel secret values present in its secret-bearing inputs, at every
/// verbosity level, and the captured host log output must never contain a
/// sentinel. The suite also proves that a verbosity raise adds no new message
/// shape (so a Debug/Trace raise cannot expand a redacted value into a
/// secret-bearing one), that the shared repetition suppressor bounds volume,
/// and that the plugin registrator wires the logging boundary without a custom
/// provider or a replaced factory.
/// </summary>
public sealed class LogRedactionTests
{
    private const string ApiKeySentinel = "SENTINEL-API-KEY-2c1f9a7b";

    private const string WebhookSecretSentinel = "SENTINEL-WEBHOOK-SECRET-9e4d";

    private const string UrlCredentialSentinel = "SENTINEL-URL-CREDENTIAL-71ab";

    private const string IdentityNameSentinel = "SENTINEL-MEDIA-NAME-33cd";

    private const string HeaderSentinel = "SENTINEL-HEADER-VALUE-58f0";

    private const string BodySentinel = "SENTINEL-REQUEST-BODY-6b2e";

    private static readonly string[] AllSentinels =
    {
        ApiKeySentinel,
        WebhookSecretSentinel,
        UrlCredentialSentinel,
        IdentityNameSentinel,
        HeaderSentinel,
        BodySentinel,
    };

    public static IEnumerable<object[]> AllVerbosityLevels()
    {
        yield return new object[] { LogVerbosity.Off };
        yield return new object[] { LogVerbosity.Error };
        yield return new object[] { LogVerbosity.Warning };
        yield return new object[] { LogVerbosity.Information };
        yield return new object[] { LogVerbosity.Debug };
        yield return new object[] { LogVerbosity.Trace };
    }

    // ---- Per-boundary sentinel redaction at every verbosity ---------------------

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task ProviderBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveProviderAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);
        Assert.Equal(Expected(verbosity, LogLevel.Warning), capturing.Records.Count);
    }

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task MatchingBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveMatchingAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);
        Assert.Equal(Expected(verbosity, LogLevel.Debug), capturing.Records.Count);
    }

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task MetadataBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveMetadataAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);
        Assert.Equal(Expected(verbosity, LogLevel.Information), capturing.Records.Count);
    }

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task ArtworkBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveArtworkAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);
        Assert.Equal(Expected(verbosity, LogLevel.Information), capturing.Records.Count);
    }

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task QueueBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveQueueAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);
        Assert.Equal(Expected(verbosity, LogLevel.Debug), capturing.Records.Count);
    }

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task ReconciliationBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveReconciliationAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);
        Assert.Equal(Expected(verbosity, LogLevel.Information), capturing.Records.Count);
    }

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task WebhookBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveWebhookAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);

        // The pre-binding filter logs the rejected authentication at Warning and
        // the hosted intake logs the resolved event at Debug, so both must be
        // observable (and nothing more).
        var expected = Expected(verbosity, LogLevel.Warning) + Expected(verbosity, LogLevel.Debug);
        Assert.Equal(expected, capturing.Records.Count);
    }

    [Theory]
    [MemberData(nameof(AllVerbosityLevels))]
    public async Task LifecycleBoundaryLogsNoSentinelAtAnyVerbosity(LogVerbosity verbosity)
    {
        var capturing = new CapturingLoggerProvider();

        await DriveLifecycleAsync(capturing, verbosity);

        AssertNoSentinel(capturing.Records);
        Assert.Equal(2 * Expected(verbosity, LogLevel.Information), capturing.Records.Count);
    }

    // ---- A verbosity raise cannot expand a redacted value -----------------------

    [Fact]
    public async Task RaisingVerbosityToTraceAddsNoMessageBeyondDebugForEveryBoundary()
    {
        foreach (var (name, drive) in BoundaryCases())
        {
            var debugCapture = new CapturingLoggerProvider();
            await drive(debugCapture, LogVerbosity.Debug);

            var traceCapture = new CapturingLoggerProvider();
            await drive(traceCapture, LogVerbosity.Trace);

            AssertNoSentinel(traceCapture.Records);

            // ArrTags never has a Trace-only branch: the same bounded, redacted
            // data shape is emitted at Trace as at Debug, so raising verbosity
            // cannot turn a redacted value into a secret-bearing one.
            Assert.Equal(
                debugCapture.Records.Select(record => record.Message).OrderBy(message => message, StringComparer.Ordinal),
                traceCapture.Records.Select(record => record.Message).OrderBy(message => message, StringComparer.Ordinal));
            Assert.True(
                traceCapture.Records.Count > 0,
                $"The '{name}' boundary emitted no record at Trace, so the comparison was vacuous.");
        }
    }

    [Fact]
    public async Task RaisingVerbosityToTraceDoesNotExposeTheWebhookHeaderOrBody()
    {
        var capturing = new CapturingLoggerProvider();

        await DriveWebhookAsync(capturing, LogVerbosity.Trace);

        AssertNoSentinel(capturing.Records);
        Assert.Contains(
            capturing.Records,
            record => record.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            capturing.Records,
            record => record.Message.Contains(ArrTagsWebhookController.SecretHeaderName, StringComparison.Ordinal));
    }

    // ---- Facade and volume-bound unit coverage ---------------------------------

    [Theory]
    [InlineData(LogVerbosity.Off, false)]
    [InlineData(LogVerbosity.Error, false)]
    [InlineData(LogVerbosity.Warning, true)]
    [InlineData(LogVerbosity.Information, true)]
    [InlineData(LogVerbosity.Debug, true)]
    [InlineData(LogVerbosity.Trace, true)]
    public void FacadeGatesEmissionOnTheConfiguredVerbosity(LogVerbosity verbosity, bool warningWritten)
    {
        var capturing = new CapturingLoggerProvider();
        var log = BuildLog<MetadataReconciliationProcessor>(capturing, verbosity);

        log.Write(LogLevel.Warning, ArrTagsLogEvent.ProviderReadFailed, "bounded warning");

        Assert.Equal(warningWritten ? 1 : 0, capturing.Records.Count);
    }

    [Fact]
    public void FacadeReportsDisabledWhenTheVerbosityIsOff()
    {
        var capturing = new CapturingLoggerProvider();
        var log = BuildLog<MetadataReconciliationProcessor>(capturing, LogVerbosity.Off);

        Assert.False(log.IsEnabled(LogLevel.Critical));
        Assert.False(log.IsEnabled(LogLevel.Trace));
    }

    [Fact]
    public void FacadeWritesOneSuppressionSummaryAfterTheWindowRollsOver()
    {
        var capturing = new CapturingLoggerProvider();
        var time = new TestTimeProvider();
        var log = BuildLog<MetadataReconciliationProcessor>(
            capturing,
            LogVerbosity.Warning,
            new LogThrottle(time));

        for (var index = 0; index < LogThrottle.MaxEmissionsPerWindow + 2; index++)
        {
            log.Write(LogLevel.Warning, ArrTagsLogEvent.ProviderReadFailed, "bounded warning " + index);
        }

        Assert.Equal(LogThrottle.MaxEmissionsPerWindow, capturing.Records.Count);

        time.UtcNow += LogThrottle.SuppressionWindow;
        log.Write(LogLevel.Warning, ArrTagsLogEvent.ProviderReadFailed, "bounded warning later");

        Assert.Equal(LogThrottle.MaxEmissionsPerWindow + 1, capturing.Records.Count);
        Assert.Contains("Suppressed 2", capturing.Records[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrottleAdmitsAtMostTheBoundPerWindow()
    {
        var throttle = new LogThrottle(new TestTimeProvider());

        for (var index = 0; index < LogThrottle.MaxEmissionsPerWindow; index++)
        {
            Assert.Equal(
                LogThrottleDecision.Emit,
                throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out _));
        }

        Assert.Equal(
            LogThrottleDecision.Suppress,
            throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out var suppressed));
        Assert.Equal(1, suppressed);

        Assert.Equal(
            LogThrottleDecision.Suppress,
            throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out suppressed));
        Assert.Equal(2, suppressed);
    }

    [Fact]
    public void ThrottleEmitsOneSuppressionSummaryWhenTheWindowRollsOver()
    {
        var time = new TestTimeProvider();
        var throttle = new LogThrottle(time);

        for (var index = 0; index < LogThrottle.MaxEmissionsPerWindow; index++)
        {
            throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out _);
        }

        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(
                LogThrottleDecision.Suppress,
                throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out _));
        }

        time.UtcNow += LogThrottle.SuppressionWindow;

        Assert.Equal(
            LogThrottleDecision.EmitSuppressionSummary,
            throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out var suppressed));
        Assert.Equal(3, suppressed);

        Assert.Equal(
            LogThrottleDecision.Emit,
            throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out _));
    }

    [Fact]
    public void ThrottleTrackingSetIsBounded()
    {
        var throttle = new LogThrottle(new TestTimeProvider());

        for (var index = 0; index < (LogThrottle.MaxTrackedKeys * 2) + 50; index++)
        {
            throttle.Acquire("ArrTags.Test" + index, ArrTagsLogEvent.ProviderReadFailed, out _);
        }

        Assert.InRange(throttle.TrackedKeyCount, 0, LogThrottle.MaxTrackedKeys);
    }

    [Fact]
    public async Task ThrottleIsThreadSafeAndBoundedUnderConcurrentAdmission()
    {
        var throttle = new LogThrottle(new TestTimeProvider());
        var emitted = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 2000),
            (_, _) =>
            {
                if (throttle.Acquire("ArrTags.Test", ArrTagsLogEvent.ProviderReadFailed, out _)
                    == LogThrottleDecision.Emit)
                {
                    Interlocked.Increment(ref emitted);
                }

                return ValueTask.CompletedTask;
            });

        Assert.InRange(emitted, 1, LogThrottle.MaxEmissionsPerWindow);
    }

    // ---- DI wiring -------------------------------------------------------------

    [Fact]
    public void RegistratorResolvesInstrumentedLogsWithArrTagsCategoryPrefixes()
    {
        var capturing = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(capturing));
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IArrTagsLog<MetadataReconciliationProcessor>>());
        Assert.NotNull(provider.GetRequiredService<IArrTagsLog<ArrTagsLifecycleService>>());
        Assert.NotNull(provider.GetRequiredService<IArrTagsLog<WebhookAuthenticationFilter>>());
        Assert.NotNull(provider.GetRequiredService<IArrTagsLog<RadarrMetadataReader>>());
        Assert.NotNull(provider.GetRequiredService<IArrTagsLog<ConcurrencyLimitedArrMetadataReader<RadarrMetadataReader>>>());

        Assert.NotEmpty(capturing.Categories);
        Assert.All(capturing.Categories, category => Assert.StartsWith("ArrTags.", category, StringComparison.Ordinal));
    }

    [Fact]
    public void RegistratorResolvesLogsWithoutHostLoggingRegistered()
    {
        var services = new ServiceCollection();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();

        // The category logger falls back to NullLogger when the host has not
        // registered logging, so registration and resolution never fail.
        var log = provider.GetRequiredService<IArrTagsLog<MetadataReconciliationProcessor>>();
        Assert.False(log.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public async Task MvcFilterConstructionInjectsTheRegisteredLogBoundary()
    {
        // The webhook authentication filter is built by MVC's TypeFilter through
        // ActivatorUtilities rather than by a factory lambda, so this proves the
        // registered log boundary reaches that construction path and the
        // rejection is still recorded.
        var capturing = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(capturing));
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var filter = ActivatorUtilities.CreateInstance<WebhookAuthenticationFilter>(provider);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ArrTagsWebhookController.SecretHeaderName] = HeaderSentinel;
        httpContext.Request.ContentType = "application/json";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        await filter.OnAuthorizationAsync(new AuthorizationFilterContext(actionContext, Array.Empty<IFilterMetadata>()));

        AssertNoSentinel(capturing.Records);
        Assert.Contains(
            capturing.Records,
            record => record.EventId == (int)ArrTagsLogEvent.WebhookAuthenticationRejected);
    }

    // ---- Boundary drivers ------------------------------------------------------

    private static IReadOnlyList<(string Name, Func<CapturingLoggerProvider, LogVerbosity, Task> Drive)> BoundaryCases()
    {
        return new (string, Func<CapturingLoggerProvider, LogVerbosity, Task>)[]
        {
            ("provider", DriveProviderAsync),
            ("matching", DriveMatchingAsync),
            ("metadata", DriveMetadataAsync),
            ("artwork", DriveArtworkAsync),
            ("queue", DriveQueueAsync),
            ("reconciliation", DriveReconciliationAsync),
            ("webhook", DriveWebhookAsync),
            ("lifecycle", DriveLifecycleAsync),
        };
    }

    private static async Task DriveProviderAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var inner = new FakeMetadataReader
        {
            Result = ArrMetadataReadResult.Failure(new ArrProviderError(
                ArrProviderErrorCode.AuthenticationFailed,
                ArrErrorRetryability.Never,
                "The provider rejected the configured credentials.")),
        };

        using var limiter = new ProviderConcurrencyLimiter(new ConfigurationSnapshotService(new PluginConfiguration()));
        var reader = new ConcurrencyLimitedArrMetadataReader<FakeMetadataReader>(
            inner,
            limiter,
            BuildLog<ConcurrencyLimitedArrMetadataReader<FakeMetadataReader>>(capturing, verbosity));

        await reader.ReadAsync(SentinelIdentity(), SentinelConnection(), CancellationToken.None);
    }

    private static async Task DriveMatchingAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var client = new FakeRadarrReadClient
        {
            Movies = ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(new[]
            {
                new RadarrMovieResource { Id = 7, Title = "Example", TmdbId = 603, MovieFileId = 42 },
            }),
        };
        var reader = new RadarrMetadataReader(
            new FakeReadClientFactory { Radarr = client },
            BuildLog<RadarrMetadataReader>(capturing, verbosity));

        await reader.ReadAsync(SentinelIdentity(), SentinelConnection(), CancellationToken.None);
    }

    private static async Task DriveMetadataAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var configuration = SentinelConfiguration();
        var store = new MetadataStateStore(new StateRepository(CreateRoot()));
        var resolver = new ReconciliationLibraryResolver();
        resolver.LibraryIds[ReconciliationFixtures.ItemId] = ReconciliationFixtures.LibraryId;
        resolver.Items[ReconciliationFixtures.ItemId] = SentinelMovie();

        var reader = new FakeMetadataReader
        {
            Kind = ArrProviderKind.Radarr,
            Handler = static (identity, connection, _) =>
            {
                var record = new RadarrIdentity(connection.ConnectionId, 42, ArrFileIdentity.Present(84));
                var match = new MediaMatch(
                    identity,
                    connection.Provider,
                    connection.ConnectionId,
                    MediaMatchStatus.Matched,
                    MediaMatchMethod.ProviderId,
                    record,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tmdb"] = "603" });
                var metadata = new BadgeMetadata(
                    connection.Provider,
                    record,
                    DateTimeOffset.UtcNow,
                    quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "Remux", 7),
                    videoCodec: "h264");
                return ArrMetadataReadResult.Success(match, metadata);
            },
        };

        var processor = new MetadataReconciliationProcessor(
            configuration,
            resolver,
            new IArrMetadataReader[] { reader },
            store,
            BuildLog<MetadataReconciliationProcessor>(capturing, verbosity));

        var key = new WorkItemKey(ReconciliationFixtures.ItemId, null, ArtworkImageSurface.Primary);
        var item = new LibraryWorkItem(key, LibraryWorkReason.Updated, configuration.Current.ConfigurationVersion);

        await processor.ReconcileAsync(item, CancellationToken.None);
    }

    private static async Task DriveArtworkAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var repository = new StateRepository(CreateRoot());
        var host = new PipelineArtworkHost();
        var artifacts = new SourceArtifactStore(repository);
        var states = new PublishedArtworkStateStore(repository);
        var operations = new ArtworkOperationStore(repository);
        var publisher = new ArtworkPublisher(host, host, artifacts, states, operations, new OperationalLimits());
        var coordinator = new ArtworkGenerationCoordinator(
            host,
            new FingerprintingRenderer(),
            publisher,
            states,
            artifacts,
            BuildLog<ArtworkGenerationCoordinator>(capturing, verbosity));

        var identity = SentinelIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        var request = new ArtworkGenerationRequest(
            identity.JellyfinItemId,
            ArtworkImageSurface.Primary,
            identity,
            match,
            metadata: null,
            Array.Empty<BadgeDefinition>(),
            configurationFingerprint: "test-configuration-fingerprint");

        await coordinator.GenerateAsync(request, CancellationToken.None);
    }

    private static async Task DriveQueueAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var configuration = SentinelConfiguration();
        using var queue = new LibraryWorkQueue(8);
        var processor = new RecordingWorkItemProcessor();
        var worker = new LibraryWorkWorker(
            queue,
            processor,
            configuration,
            workerCount: 1,
            boundedShutdownTimeout: TimeSpan.FromSeconds(2),
            log: BuildLog<LibraryWorkWorker>(capturing, verbosity));

        await worker.StartAsync(CancellationToken.None);
        queue.TryEnqueue(new LibraryWorkHint(
            ReconciliationFixtures.ItemId,
            LibraryWorkReason.Updated,
            configuration.Current.ConfigurationVersion));

        await processor.Processed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
    }

    private static async Task DriveReconciliationAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var configuration = SentinelConfiguration();
        var fences = new ArtworkLifecycleFenceStore(new StateRepository(CreateRoot()));
        var resolver = new ReconciliationLibraryResolver();
        resolver.LibraryIds[ReconciliationFixtures.ItemId] = ReconciliationFixtures.LibraryId;
        var movie = SentinelMovie();
        resolver.Items[ReconciliationFixtures.ItemId] = movie;

        var enumerator = new FakeMediaLibraryEnumerator();
        enumerator.Items.Add(movie);
        var sink = new RecordingWorkHintSink();

        var service = new LibraryReconciliationService(
            configuration,
            resolver,
            enumerator,
            sink,
            fences,
            BuildLog<LibraryReconciliationService>(capturing, verbosity));

        await service.ReconcileAsync(LibraryReconciliationSource.Scheduled, progress: null, CancellationToken.None);
    }

    private static async Task DriveWebhookAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var configuration = SentinelConfiguration();

        // Pre-binding authentication filter: a rejected request whose header and
        // body both carry sentinels.
        var filter = new WebhookAuthenticationFilter(
            configuration,
            configuration,
            BuildLog<WebhookAuthenticationFilter>(capturing, verbosity));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ArrTagsWebhookController.SecretHeaderName] = HeaderSentinel;
        httpContext.Request.ContentType = "application/json";
        var bodyBytes = Encoding.UTF8.GetBytes(BodySentinel);
        httpContext.Request.Body = new MemoryStream(bodyBytes);
        httpContext.Request.ContentLength = bodyBytes.Length;
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        await filter.OnAuthorizationAsync(new AuthorizationFilterContext(actionContext, Array.Empty<IFilterMetadata>()));

        // Hosted intake: an authenticated event is resolved off the request path.
        var store = new MetadataStateStore(new StateRepository(CreateRoot()));
        var resolver = new WebhookReconciliationResolver(store);
        var sink = new RecordingWorkHintSink();
        using var intake = new WebhookIntake(4);
        using var service = new WebhookIntakeService(
            intake,
            resolver,
            sink,
            configuration,
            TimeSpan.FromSeconds(2),
            BuildLog<WebhookIntakeService>(capturing, verbosity));

        await service.StartAsync(CancellationToken.None);
        intake.TrySubmit(new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42));

        var wait = Expected(verbosity, LogLevel.Debug) > 0
            ? TimeSpan.FromSeconds(5)
            : TimeSpan.FromMilliseconds(200);
        await WaitForRecordAsync(capturing, ArrTagsLogEvent.WebhookEventResolved, wait);

        await service.StopAsync(CancellationToken.None);
    }

    private static async Task DriveLifecycleAsync(CapturingLoggerProvider capturing, LogVerbosity verbosity)
    {
        var configuration = SentinelConfiguration();
        var service = new ArrTagsLifecycleService(
            new FakeLibraryEventSource(),
            new NoOpLifecycleCoordinator(),
            configuration,
            new RecordingWorkHintSink(),
            TimeSpan.FromSeconds(2),
            BuildLog<ArrTagsLifecycleService>(capturing, verbosity));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    // ---- Helpers ---------------------------------------------------------------

    private static IArrTagsLog<T> BuildLog<T>(
        CapturingLoggerProvider capturing,
        LogVerbosity verbosity,
        LogThrottle? throttle = null)
    {
        var gate = new LogVerbosityGate(new ConfigurationSnapshotService(new PluginConfiguration
        {
            LogVerbosity = verbosity,
        }));

        return new ArrTagsLog<T>(
            capturing.CreateLogger<T>(),
            gate,
            throttle ?? new LogThrottle(new TestTimeProvider()));
    }

    private static ConfigurationSnapshotService SentinelConfiguration()
    {
        return new ConfigurationSnapshotService(new PluginConfiguration
        {
            BadgeMoviePosters = true,
            WebhookSecret = WebhookSecretSentinel,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = ApiKeySentinel,
            },
        });
    }

    private static MediaIdentity SentinelIdentity()
    {
        return new MediaIdentity(
            ReconciliationFixtures.ItemId,
            MediaItemType.Movie,
            ReconciliationFixtures.LibraryId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tmdb"] = "603",
                ["Imdb"] = "tt0133093",
            },
            IdentityNameSentinel,
            2021,
            mediaLocation: new MediaLocationSummary(
                MediaLocationKind.FileSystem,
                isFileProtocol: true,
                mediaSourceCount: 1,
                primaryPath: "/media/movies/example.mkv"),
            sourceFingerprint: "SOURCE-FINGERPRINT");
    }

    private static ReconciliationTestMovie SentinelMovie()
    {
        var movie = ReconciliationFixtures.Movie(ReconciliationFixtures.ItemId, ReconciliationFixtures.LibraryId);
        movie.Name = IdentityNameSentinel;
        return movie;
    }

    private static ArrConnection SentinelConnection()
    {
        var baseUrl = "http://user:" + UrlCredentialSentinel + "@radarr.test:7878";
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, baseUrl);
        var provider = new ArrProvider(ArrProviderKind.Radarr, connectionId.Value);

        return new ArrConnection(
            connectionId,
            provider,
            baseUrl,
            enabled: true,
            requestTimeoutSeconds: 15,
            ArrTlsPolicy.Strict,
            SecretReference.RadarrApiKey,
            configurationVersion: 1,
            hasApiKey: true,
            ArrConnectionHealth.Unknown,
            lastProbedAt: null);
    }

    private static int Expected(LogVerbosity verbosity, LogLevel level)
    {
        var minimum = LogVerbosityGate.ToLogLevel(verbosity);
        return minimum != LogLevel.None && level >= minimum ? 1 : 0;
    }

    private static void AssertNoSentinel(IReadOnlyList<CapturedRecord> records)
    {
        foreach (var record in records)
        {
            foreach (var sentinel in AllSentinels)
            {
                Assert.DoesNotContain(sentinel, record.Message, StringComparison.Ordinal);
            }
        }
    }

    private static async Task WaitForRecordAsync(
        CapturingLoggerProvider capturing,
        ArrTagsLogEvent logEvent,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (capturing.Records.Any(record => record.EventId == (int)logEvent))
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "arrtags-log-redaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // ---- Test doubles ----------------------------------------------------------

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingWorkItemProcessor : IWorkItemProcessor
    {
        public TaskCompletionSource<LibraryWorkItem> Processed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
        {
            Processed.TrySetResult(item);
            return Task.FromResult(WorkProcessingResult.Completed("The test work completed."));
        }
    }

    private sealed class NoOpLifecycleCoordinator : IArtworkLifecycleCoordinator
    {
        public void ResetStaleFence()
        {
        }

        public Task<ArtworkLifecycleResult> DrainAsync(ArtworkLifecycleFence fence, CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkLifecycleResult.Create(
                fence,
                ArtworkLifecycleOutcome.NothingToDo,
                "No lifecycle fence was active."));
        }

        public Task<ArtworkLifecycleResult> DrainForHostShutdownAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkLifecycleResult.Create(
                ArtworkLifecycleFence.Normal,
                ArtworkLifecycleOutcome.NothingToDo,
                "No lifecycle fence was active."));
        }

        public Task<ArtworkRemovalResult> HandleItemRemovedAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
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
        public ArrProviderKind Kind => ArrProviderKind.Radarr;

        public ArrConnection Connection { get; } = SentinelConnection();

        public ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>> Movies { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(Array.Empty<RadarrMovieResource>());

        public ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>> Files { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(Array.Empty<RadarrMovieFileResource>());

        public Task<ArrConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>>> GetMoviesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Movies);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(
            int movieId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Files);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new();
        private readonly List<CapturedRecord> _records = new();
        private readonly List<string> _categories = new();

        public IReadOnlyList<CapturedRecord> Records
        {
            get
            {
                lock (_gate)
                {
                    return _records.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Categories
        {
            get
            {
                lock (_gate)
                {
                    return _categories.ToArray();
                }
            }
        }

        public ILogger<T> CreateLogger<T>()
        {
            return new CapturingLogger<T>(this, typeof(T).FullName ?? typeof(T).Name);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(this, categoryName);
        }

        public void Dispose()
        {
        }

        internal void Add(CapturedRecord record)
        {
            lock (_gate)
            {
                _records.Add(record);
            }
        }

        internal void RecordCategory(string category)
        {
            lock (_gate)
            {
                _categories.Add(category);
            }
        }
    }

    private class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _provider;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
            provider.RecordCategory(category);
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _provider.Add(new CapturedRecord(_category, logLevel, eventId.Id, formatter(state, exception)));
        }
    }

    private sealed class CapturingLogger<T> : CapturingLogger, ILogger<T>
    {
        public CapturingLogger(CapturingLoggerProvider provider, string category)
            : base(provider, category)
        {
        }
    }

    private sealed record CapturedRecord(string Category, LogLevel Level, int EventId, string Message);
}
