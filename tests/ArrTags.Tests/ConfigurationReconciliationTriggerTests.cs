using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.PluginLifecycle;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.State;
using ArrTags.Updates;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 9 task 9.4 coverage for the bounded, non-blocking post-save
/// reconciliation trigger (ADR-016 clause 5 second bullet). These tests drive
/// the real <see cref="Plugin.UpdateConfiguration"/> override and the real
/// <see cref="ConfigurationReconciliationTrigger"/>: a successful save requests
/// exactly one bounded reconciliation, an invalid save requests none, the save
/// never throws or waits for the reconciliation, redundant requests coalesce to
/// a bounded rerun, and an existing published poster re-renders with the saved
/// settings instead of waiting for the next scheduled run. No live Jellyfin or
/// Arr instance is required.
/// </summary>
public sealed class ConfigurationReconciliationTriggerTests
{
    [Fact]
    public void SuccessfulSaveRequestsExactlyOneBoundedReconciliation()
    {
        var trigger = new RecordingReconciliationTrigger();
        using var harness = new SaveHarness(trigger);

        harness.PluginInstance.UpdateConfiguration(ValidCandidate());

        // The save activated the replacement and requested exactly one bounded
        // reconciliation through the plugin-owned boundary.
        Assert.Equal(1, trigger.RequestCount);
        Assert.Equal(2, harness.Snapshot.Current.ConfigurationVersion);
        Assert.True(harness.Snapshot.Current.RadarrEnabled);
    }

    [Fact]
    public void InvalidSaveRequestsNoReconciliation()
    {
        var trigger = new RecordingReconciliationTrigger();
        using var harness = new SaveHarness(trigger);

        var invalid = new PluginConfiguration
        {
            WebhookSecret = "rejected-webhook-secret-3f9d",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "not-a-url",
                ApiKey = "rejected-radarr-key-1b2e",
            },
        };

        harness.PluginInstance.UpdateConfiguration(invalid);

        // A rejected save is never activated and must not request reconciliation.
        Assert.Equal(0, trigger.RequestCount);
        Assert.Equal(1, harness.Snapshot.Current.ConfigurationVersion);
    }

    [Fact]
    public void SaveDoesNotThrowWhenTheReconciliationTriggerThrows()
    {
        using var harness = new SaveHarness(new ThrowingReconciliationTrigger());

        // A failing trigger is contained: the save still activates the
        // replacement and never throws into the host.
        Assert.Null(Record.Exception(() => harness.PluginInstance.UpdateConfiguration(ValidCandidate())));
        Assert.Equal(2, harness.Snapshot.Current.ConfigurationVersion);
    }

    [Fact]
    public void ValidButUnactivatedSaveRequestsNoReconciliation()
    {
        var trigger = new RecordingReconciliationTrigger();

        // The running snapshot service is unavailable, so the valid candidate is
        // persisted but cannot be activated at runtime.
        using var harness = new SaveHarness(trigger, registerSnapshotService: false);

        Assert.Null(Record.Exception(() => harness.PluginInstance.UpdateConfiguration(ValidCandidate())));

        // The candidate is valid and persisted, but no replacement was activated,
        // so no reconciliation is requested.
        Assert.True(harness.PluginInstance.LastConfigurationValidationResult!.IsValid);
        Assert.Equal(0, trigger.RequestCount);
        Assert.Equal(1, harness.Snapshot.Current.ConfigurationVersion);
    }

    [Fact]
    public async Task SaveDoesNotWaitForTheReconciliationScanToComplete()
    {
        using var triggerHarness = new TriggerHarness(batchSize: 10);
        triggerHarness.AddMovie();

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim(false);
        triggerHarness.Enumerator.OnEnumerate = _ =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        using var saveHarness = new SaveHarness(triggerHarness.Trigger);
        await triggerHarness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            var save = Task.Run(() => saveHarness.PluginInstance.UpdateConfiguration(ValidCandidate()));

            // The reconciliation scan has started and is blocked in the library
            // enumeration (the enumerator hook is waiting on the release signal).
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The load-bearing assertion: the save response completes even though
            // the bounded full-library scan has not completed and cannot complete
            // until the enumerator is released. A synchronous scan on the save
            // path would deadlock here and this bounded wait would time out.
            await save.WaitAsync(TimeSpan.FromSeconds(5));

            release.Set();
            await Phase6Wait.UntilAsync(() => triggerHarness.Sink.Count == 1);
            Assert.Equal(1, triggerHarness.Sink.Count);
        }
        finally
        {
            release.Set();
            await triggerHarness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReconciliationEnqueuesBoundedHintsAtTheCurrentSnapshotVersion()
    {
        using var harness = new TriggerHarness(batchSize: 2);
        var first = harness.AddMovie();
        var second = harness.AddMovie();
        var third = harness.AddMovie();
        var version = harness.Configuration.Current.ConfigurationVersion;

        await harness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            harness.Trigger.RequestReconciliation();

            await Phase6Wait.UntilAsync(() => harness.Sink.Count == 3);

            Assert.Equal(3, harness.Sink.Count);
            Assert.All(harness.Sink.Hints, hint => Assert.Equal(LibraryWorkReason.Reconciliation, hint.Reason));
            Assert.All(harness.Sink.Hints, hint => Assert.Equal(version, hint.ConfigurationVersion));
            Assert.Equal(
                new[] { first, second, third }.OrderBy(id => id),
                harness.Sink.Hints.Select(hint => hint.ItemId).OrderBy(id => id));

            // The scan is bounded by the configured batch size, not by the item
            // count: three items at a batch size of two request two pages.
            Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Enumerator.Requests);
        }
        finally
        {
            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RapidRequestsCoalesceIntoABoundedRerun()
    {
        using var harness = new TriggerHarness(batchSize: 10);
        harness.AddMovie();

        var invocations = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new ManualResetEventSlim(false);
        var secondRelease = new ManualResetEventSlim(false);
        harness.Enumerator.OnEnumerate = _ =>
        {
            var index = Interlocked.Increment(ref invocations);
            if (index == 1)
            {
                firstEntered.TrySetResult();
                firstRelease.Wait(TimeSpan.FromSeconds(10));
            }
            else if (index == 2)
            {
                secondEntered.TrySetResult();
                secondRelease.Wait(TimeSpan.FromSeconds(10));
            }
        };

        await harness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            harness.Trigger.RequestReconciliation();
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Many requests while the first reconciliation is running must not
            // grow an unbounded queue: they coalesce to one bounded rerun.
            for (var index = 0; index < 8; index++)
            {
                harness.Trigger.RequestReconciliation();
            }

            firstRelease.Set();
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            secondRelease.Set();

            // The coalesced requests produce exactly one bounded rerun: the
            // enumeration count must stay at two for a bounded stability window
            // (a third full reconciliation would begin immediately and be
            // observed here).
            await AssertStableAsync(() => Volatile.Read(ref invocations), expected: 2);
        }
        finally
        {
            firstRelease.Set();
            secondRelease.Set();
            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopAsyncIsBoundedWhenTheReconciliationIsBlocked()
    {
        using var harness = new TriggerHarness(batchSize: 10);
        harness.AddMovie();

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim(false);
        harness.Enumerator.OnEnumerate = _ =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        await harness.Trigger.StartAsync(CancellationToken.None);
        harness.Trigger.RequestReconciliation();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The scan is blocked inside the synchronous library enumeration, which
        // cancellation cannot interrupt. StopAsync must still return within its
        // bounded shutdown timeout (the harness configures two seconds).
        var stopwatch = Stopwatch.StartNew();
        await harness.Trigger.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(4));
        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"StopAsync must return within the bounded shutdown timeout, but took {stopwatch.Elapsed}.");

        // The loop observes the cancellation: once the blocked enumeration is
        // released it must not enqueue any work.
        release.Set();
        await AssertStableAsync(() => harness.Sink.Count, expected: 0);
    }

    [Fact]
    public async Task ExistingPublishedPosterReRendersAfterTheSave()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();

        // Publish the initial poster under configuration version 1.
        await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);
        Assert.Equal(1, harness.Host.SaveCalls);
        Assert.Equal(1, harness.Renderer.Calls);
        var firstFingerprint = harness.States.Read(harness.ItemId, Phase6Harness.Surface).Value!.PublishedFingerprint;
        var firstConfigurationFingerprint = harness.Renderer.LastRequest!.ConfigurationFingerprint;

        // Simulate the successful save: activate a replacement that changes an
        // output-affecting renderer setting.
        var replacement = new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
            Renderer = new RendererConfiguration
            {
                Selectors = new Collection<BadgeSelectorConfiguration>
                {
                    new BadgeSelectorConfiguration
                    {
                        Selector = BadgeSelector.Quality,
                        Enabled = true,
                        Template = "Q{value}",
                    },
                },
            },
        };

        Assert.True(harness.Configuration.TryReplace(replacement, out _));
        var savedVersion = harness.Configuration.Current.ConfigurationVersion;
        Assert.Equal(2, savedVersion);

        // A work item carrying the pre-save version is skipped by the publishing
        // processor, so without the trigger the published poster would not be
        // re-rendered until the next library event, webhook, post-scan, or
        // scheduled run.
        var stale = new LibraryWorkItem(
            new WorkItemKey(harness.ItemId, null, Phase6Harness.Surface),
            LibraryWorkReason.Updated,
            savedVersion - 1);
        await harness.Publishing.ProcessAsync(stale, CancellationToken.None);
        Assert.Equal(1, harness.Host.SaveCalls);
        Assert.Equal(1, harness.Renderer.Calls);

        // The post-save trigger enqueues a fresh work item at the current version.
        var enumerator = new FakeMediaLibraryEnumerator();
        enumerator.Items.Add(harness.Library.Items[harness.ItemId]);
        var service = new LibraryReconciliationService(
            harness.Configuration,
            harness.Library,
            enumerator,
            harness.Queue,
            harness.Fences);
        using var trigger = new ConfigurationReconciliationTrigger(service, TimeSpan.FromSeconds(2));
        await trigger.StartAsync(CancellationToken.None);
        try
        {
            trigger.RequestReconciliation();
            await Phase6Wait.UntilAsync(() => harness.Queue.Count == 1);

            var workItem = await harness.Queue.DequeueAsync(CancellationToken.None);
            Assert.Equal(savedVersion, workItem.ConfigurationVersion);

            await harness.Publishing.ProcessAsync(workItem, CancellationToken.None);

            // The existing published poster re-rendered with the saved settings:
            // a second render and publication occurred with the new renderer
            // configuration, and the published fingerprint changed.
            Assert.Equal(2, harness.Host.SaveCalls);
            Assert.Equal(2, harness.Renderer.Calls);
            Assert.NotEqual(firstConfigurationFingerprint, harness.Renderer.LastRequest!.ConfigurationFingerprint);
            Assert.Contains(
                harness.Renderer.LastRequest.BadgeDefinitions,
                definition => definition.Selector == BadgeSelector.Quality && definition.Template == "Q{value}");
            var secondFingerprint = harness.States.Read(harness.ItemId, Phase6Harness.Surface).Value!.PublishedFingerprint;
            Assert.NotNull(secondFingerprint);
            Assert.NotEqual(firstFingerprint, secondFingerprint);
        }
        finally
        {
            await trigger.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void RegistratorRegistersThePostSaveTriggerAsAHostedService()
    {
        var services = new ServiceCollection();

        // The host boundaries the full registrator's hosted services resolve, but
        // which a host-free test must supply: the media library enumerator, the
        // artwork recovery gate, the artwork host source/image boundary, and the
        // lifecycle coordinator. The stubs are never invoked because no hosted
        // service is started.
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());
        services.AddSingleton<IMediaLibraryResolver>(new ReconciliationLibraryResolver());
        services.AddSingleton<IMediaLibraryEnumerator>(new FakeMediaLibraryEnumerator());
        services.AddSingleton<IArtworkLifecycleCoordinator>(new ThrowingArtworkLifecycleCoordinator());
        services.AddSingleton<IArtworkRecoveryGate>(new ThrowingArtworkRecoveryGate());
        var artworkHost = new PipelineArtworkHost();
        services.AddSingleton<IArtworkSourceReader>(artworkHost);
        services.AddSingleton<IArtworkImageWriter>(artworkHost);

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToArray();

        // The production registration wires the trigger as a hosted service.
        // Without it RequestReconciliation silently no-ops (the started flag is
        // never set) and post-save reconciliation would never run in the shipped
        // host, so this assertion is load-bearing.
        var hostedTrigger = Assert.Single(hosted.OfType<ConfigurationReconciliationTrigger>());

        // The hosted service and the save path's IConfigurationReconciliationTrigger
        // are the same singleton, so a save request reaches the started loop.
        Assert.Same(hostedTrigger, provider.GetRequiredService<IConfigurationReconciliationTrigger>());
        Assert.Same(hostedTrigger, provider.GetRequiredService<ConfigurationReconciliationTrigger>());

        // Constructing the provider and enumerating the hosted services does not
        // start them: no reconciliation work was enqueued.
        Assert.Equal(0, provider.GetRequiredService<IWorkHintSink>().Count);
    }

    private static PluginConfiguration ValidCandidate()
    {
        return new PluginConfiguration
        {
            WebhookSecret = "stable-webhook-secret-7a1c",
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "https://radarr.local:7878",
                ApiKey = "radarr-key",
            },
        };
    }

    /// <summary>
    /// Polls <paramref name="observe"/> for a bounded stability window and fails
    /// if it ever differs from <paramref name="expected"/>. It replaces a fixed
    /// delay with a deterministic check that an undesired extra state change does
    /// not occur during the window.
    /// </summary>
    /// <param name="observe">The observed value.</param>
    /// <param name="expected">The value that must hold throughout the window.</param>
    /// <param name="stabilityMilliseconds">The bounded stability window.</param>
    /// <returns>A task that completes when the window elapses.</returns>
    private static async Task AssertStableAsync(Func<int> observe, int expected, int stabilityMilliseconds = 300)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(stabilityMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Assert.Equal(expected, observe());
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A lifecycle coordinator double that fails if it is ever invoked. It only
    /// satisfies the hosted lifecycle service's constructor; no hosted service is
    /// started in the registrator test.
    /// </summary>
    private sealed class ThrowingArtworkLifecycleCoordinator : IArtworkLifecycleCoordinator
    {
        public void ResetStaleFence() => throw new NotSupportedException();

        public Task<ArtworkLifecycleResult> DrainAsync(ArtworkLifecycleFence fence, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ArtworkLifecycleResult> DrainForHostShutdownAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ArtworkRemovalResult> HandleItemRemovedAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// A recovery gate double that fails if it is ever invoked. It only satisfies
    /// the hosted startup-recovery service's constructor; no hosted service is
    /// started in the registrator test.
    /// </summary>
    private sealed class ThrowingArtworkRecoveryGate : IArtworkRecoveryGate
    {
        public Task<ArtworkRecoveryGateResult> EnsureRecoveredAsync(
            Guid jellyfinItemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ArtworkRecoveryScanResult> RecoverStartupAsync(
            int batchSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// A recording <see cref="IConfigurationReconciliationTrigger"/> double that
    /// counts the bounded requests made by the save path.
    /// </summary>
    private sealed class RecordingReconciliationTrigger : IConfigurationReconciliationTrigger
    {
        private int _requests;

        public int RequestCount => Volatile.Read(ref _requests);

        public void RequestReconciliation() => Interlocked.Increment(ref _requests);
    }

    /// <summary>
    /// A trigger double that always fails, to prove the save path contains a
    /// failing trigger.
    /// </summary>
    private sealed class ThrowingReconciliationTrigger : IConfigurationReconciliationTrigger
    {
        public void RequestReconciliation() => throw new InvalidOperationException("The trigger failed.");
    }

    /// <summary>
    /// A plugin harness with a real XML configuration file and a service provider
    /// that resolves the configuration snapshot singleton and the supplied
    /// post-save reconciliation trigger, mirroring the host's plugin construction
    /// and elevation-gated save path.
    /// </summary>
    private sealed class SaveHarness : IDisposable
    {
        private readonly string _root;

        public SaveHarness(IConfigurationReconciliationTrigger trigger, bool registerSnapshotService = true)
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-postsave-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "plugins"));
            Directory.CreateDirectory(Path.Combine(_root, "configurations"));

            Paths = CreateApplicationPaths(_root);
            Serializer = new ConfigurationActivationTests.TestXmlSerializer();

            var initial = new PluginConfiguration { WebhookSecret = "stable-webhook-secret-7a1c" };
            Serializer.SerializeToFile(initial, ConfigurationFilePath);

            Snapshot = new ConfigurationSnapshotService(initial);

            var services = new ServiceCollection();
            if (registerSnapshotService)
            {
                services.AddSingleton(Snapshot);
            }

            services.AddSingleton(trigger);

            Provider = services.BuildServiceProvider();
            PluginInstance = new Plugin(Paths, Serializer, Provider);
        }

        public IApplicationPaths Paths { get; }

        public ConfigurationActivationTests.TestXmlSerializer Serializer { get; }

        public ConfigurationSnapshotService Snapshot { get; }

        public ServiceProvider Provider { get; }

        public Plugin PluginInstance { get; }

        private string ConfigurationFilePath => Path.Combine(_root, "configurations", "ArrTags.xml");

        public void Dispose()
        {
            Provider.Dispose();
            Serializer.Dispose();
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
        }

        private static IApplicationPaths CreateApplicationPaths(string root)
        {
            var paths = DispatchProxy.Create<IApplicationPaths, ConfigurationActivationTests.TestApplicationPaths>();
            ((ConfigurationActivationTests.TestApplicationPaths)(object)paths).RootPath = root;
            return paths;
        }
    }

    /// <summary>
    /// A harness for the real post-save trigger over the real bounded
    /// reconciliation service and an in-memory library, with a recording work
    /// hint sink.
    /// </summary>
    private sealed class TriggerHarness : IDisposable
    {
        private readonly string _root;

        public TriggerHarness(int batchSize = 100)
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-postsave-trigger-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            Configuration = new ConfigurationSnapshotService(new PluginConfiguration
            {
                BadgeMoviePosters = true,
                Radarr = new ArrConnectionConfiguration
                {
                    Enabled = true,
                    BaseUrl = "http://radarr.test",
                    ApiKey = "test-radarr-api-key",
                },
                Limits = new OperationalLimits { ReconciliationBatchSize = batchSize },
            });

            Repository = new StateRepository(_root, Configuration.Current.Limits);
            Fences = new ArtworkLifecycleFenceStore(Repository);
            Resolver = new ReconciliationLibraryResolver();
            Enumerator = new FakeMediaLibraryEnumerator();
            Sink = new RecordingWorkHintSink();

            var service = new LibraryReconciliationService(
                Configuration,
                Resolver,
                Enumerator,
                Sink,
                Fences);

            Trigger = new ConfigurationReconciliationTrigger(service, TimeSpan.FromSeconds(2));
        }

        public ConfigurationSnapshotService Configuration { get; }

        public StateRepository Repository { get; }

        public ArtworkLifecycleFenceStore Fences { get; }

        public ReconciliationLibraryResolver Resolver { get; }

        public FakeMediaLibraryEnumerator Enumerator { get; }

        public RecordingWorkHintSink Sink { get; }

        public ConfigurationReconciliationTrigger Trigger { get; }

        public Guid AddMovie()
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Movie(id, ReconciliationFixtures.LibraryId);
            Resolver.LibraryIds[id] = ReconciliationFixtures.LibraryId;
            Resolver.Items[id] = item;
            Enumerator.Items.Add(item);
            return id;
        }

        public void Dispose()
        {
            Trigger.Dispose();

            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
        }
    }
}
