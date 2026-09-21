using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Concurrency;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.PluginLifecycle;
using ArrTags.Reconciliation;
using ArrTags.State;
using ArrTags.Updates;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 6.9 scheduled, manual, and post-scan
/// reconciliation triggers. They prove the triggers enumerate the configured
/// scope in bounded pages through the media library boundary, enqueue only the
/// bounded provider-neutral work hints, honor cancellation and the lifecycle
/// fence, and are wired by the plugin service registrator. No live Jellyfin or
/// Arr instance is required.
/// </summary>
public sealed class ReconciliationTriggerTests
{
    [Fact]
    public async Task ReconcilesEligibleMoviesInBatchesOfTheConfiguredSize()
    {
        using var harness = new ReconciliationHarness(batchSize: 2);
        var first = harness.AddMovie();
        var second = harness.AddMovie();
        var third = harness.AddMovie();
        var fourth = harness.AddMovie();
        var fifth = harness.AddMovie();

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(5, result.Inspected);
        Assert.Equal(5, result.Eligible);
        Assert.Equal(5, result.Enqueued);
        Assert.Equal(5, harness.Sink.Hints.Count);
        Assert.All(harness.Sink.Hints, hint => Assert.Equal(LibraryWorkReason.Reconciliation, hint.Reason));
        Assert.Equal(
            new[] { first, second, third, fourth, fifth }.OrderBy(id => id),
            harness.Sink.Hints.Select(hint => hint.ItemId).OrderBy(id => id));

        // Every page is bounded by the configured reconciliation batch size.
        Assert.Equal(new[] { (0, 2), (2, 2), (4, 2) }, harness.Enumerator.Requests);
    }

    [Fact]
    public async Task EnqueuesTheSameBoundedHintsThroughTheRealWorkQueue()
    {
        using var harness = new ReconciliationHarness(batchSize: 1);
        harness.AddMovie();
        harness.AddMovie();

        var service = new LibraryReconciliationService(
            harness.Configuration,
            harness.Resolver,
            harness.Enumerator,
            harness.Queue,
            harness.Fences);

        var result = await service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, result.Enqueued);
        Assert.Equal(2, harness.Queue.Count);
    }

    [Fact]
    public async Task SkipsItemsOutsideTheConfiguredScopeOrBadgeSurface()
    {
        using var harness = new ReconciliationHarness(batchSize: 100);
        var eligibleMovie = harness.AddMovie();
        harness.AddRemoteMovie();
        harness.AddSeriesOnly();

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(3, result.Inspected);
        Assert.Equal(1, result.Eligible);
        Assert.Equal(1, result.Enqueued);
        Assert.Equal(eligibleMovie, Assert.Single(harness.Sink.Hints).ItemId);
    }

    [Fact]
    public async Task EnqueuesEligibleEpisodes()
    {
        using var harness = new ReconciliationHarness(batchSize: 100, badgeEpisodePosters: true);
        var series = Guid.NewGuid();
        var episode = harness.AddEpisode(series);

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.PostScan,
            progress: null,
            CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
        Assert.Equal(episode, Assert.Single(harness.Sink.Hints).ItemId);
    }

    [Fact]
    public async Task SkipsWhenNoProviderIsEnabled()
    {
        using var harness = new ReconciliationHarness(batchSize: 100, radarrEnabled: false);
        harness.AddMovie();

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.NoEnabledProvider, result.Outcome);
        Assert.Empty(harness.Sink.Hints);
        Assert.Empty(harness.Enumerator.Requests);
    }

    [Fact]
    public async Task RefusesWorkWhenTheLifecycleFenceIsRaised()
    {
        using var harness = new ReconciliationHarness(batchSize: 100);
        harness.AddMovie();
        harness.Fences.Set(ArtworkLifecycleFence.Disable, "test disable");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Fenced, result.Outcome);
        Assert.Empty(harness.Sink.Hints);
        Assert.Empty(harness.Enumerator.Requests);
    }

    [Fact]
    public async Task StopsAtTheFenceBetweenBatches()
    {
        using var harness = new ReconciliationHarness(batchSize: 2);
        harness.AddMovie();
        harness.AddMovie();
        harness.AddMovie();
        harness.AddMovie();

        // Raise the fence while the first page is being enumerated; the second
        // page must never be requested or enqueued.
        harness.Enumerator.OnEnumerate = start =>
        {
            if (start == 0)
            {
                harness.Fences.Set(ArtworkLifecycleFence.Uninstall, "test uninstall");
            }
        };

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Fenced, result.Outcome);
        Assert.Equal(2, result.Enqueued);
        Assert.Equal(2, harness.Sink.Hints.Count);
        Assert.Equal(new[] { (0, 2) }, harness.Enumerator.Requests);
    }

    [Fact]
    public async Task HonorsCancellationBetweenPages()
    {
        using var harness = new ReconciliationHarness(batchSize: 2);
        harness.AddMovie();
        harness.AddMovie();
        harness.AddMovie();
        harness.AddMovie();

        using var cts = new CancellationTokenSource();
        harness.Enumerator.OnEnumerate = start =>
        {
            if (start == 2)
            {
#pragma warning disable CA1849 // A synchronous enumeration hook cannot await CancelAsync.
                cts.Cancel();
#pragma warning restore CA1849
            }
        };

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            cts.Token);

        Assert.Equal(LibraryReconciliationOutcome.Cancelled, result.Outcome);
        Assert.Equal(2, result.Enqueued);
        Assert.Equal(2, harness.Sink.Hints.Count);
    }

    [Fact]
    public async Task HonorsAnAlreadyCancelledToken()
    {
        using var harness = new ReconciliationHarness(batchSize: 2);
        harness.AddMovie();

        var token = new CancellationToken(canceled: true);

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            token);

        Assert.Equal(LibraryReconciliationOutcome.Cancelled, result.Outcome);
        Assert.Empty(harness.Sink.Hints);
    }

    [Fact]
    public async Task ReportsBoundedProgress()
    {
        using var harness = new ReconciliationHarness(batchSize: 2);
        harness.AddMovie();
        harness.AddMovie();
        harness.AddMovie();
        var progress = new RecordingProgress();

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(0d, progress.Values[0]);
        Assert.Equal(100d, progress.Values[^1]);
        Assert.All(progress.Values, value => Assert.InRange(value, 0d, 100d));
    }

    [Fact]
    public void ScheduledTaskExposesPeriodicTriggerAndManualSurface()
    {
        using var harness = new ReconciliationHarness(batchSize: 100);
        var task = new ArrTagsReconciliationTask(harness.Service);

        Assert.Equal(ArrTagsReconciliationTask.TaskKey, task.Key);
        Assert.Equal(ArrTagsReconciliationTask.TaskCategory, task.Category);
        Assert.False(string.IsNullOrWhiteSpace(task.Name));
        Assert.False(string.IsNullOrWhiteSpace(task.Description));

        var trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.IntervalTrigger, trigger.Type);
        Assert.Equal(ArrTagsReconciliationTask.DefaultInterval.Ticks, trigger.IntervalTicks);
    }

    [Fact]
    public async Task ScheduledTaskDelegatesToTheBoundedReconciliation()
    {
        using var harness = new ReconciliationHarness(batchSize: 100);
        harness.AddMovie();
        var task = new ArrTagsReconciliationTask(harness.Service);

        await task.ExecuteAsync(new RecordingProgress(), CancellationToken.None);

        var hint = Assert.Single(harness.Sink.Hints);
        Assert.Equal(LibraryWorkReason.Reconciliation, hint.Reason);
    }

    [Fact]
    public async Task ScheduledTaskPropagatesCancellation()
    {
        using var harness = new ReconciliationHarness(batchSize: 100);
        harness.AddMovie();
        var task = new ArrTagsReconciliationTask(harness.Service);

        var token = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => task.ExecuteAsync(new RecordingProgress(), token));
    }

    [Fact]
    public async Task PostScanTaskDelegatesToTheBoundedReconciliation()
    {
        using var harness = new ReconciliationHarness(batchSize: 100);
        harness.AddMovie();
        var task = new ArrTagsPostScanTask(harness.Service);

        await task.Run(new RecordingProgress(), CancellationToken.None);

        Assert.Single(harness.Sink.Hints);
    }

    [Fact]
    public async Task PostScanTaskPropagatesCancellation()
    {
        using var harness = new ReconciliationHarness(batchSize: 100);
        harness.AddMovie();
        var task = new ArrTagsPostScanTask(harness.Service);

        var token = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => task.Run(new RecordingProgress(), token));
    }

    [Fact]
    public void TasksAreDiscoverableByJellyfinAssemblyScanning()
    {
        // Jellyfin discovers concrete public plugin types that implement the
        // scheduled-task and post-scan contracts by scanning the plugin assembly.
        Assert.True(typeof(IScheduledTask).IsAssignableFrom(typeof(ArrTagsReconciliationTask)));
        Assert.True(typeof(ArrTagsReconciliationTask).IsPublic);
        Assert.False(typeof(ArrTagsReconciliationTask).IsAbstract);
        Assert.True(typeof(ILibraryPostScanTask).IsAssignableFrom(typeof(ArrTagsPostScanTask)));
        Assert.True(typeof(ArrTagsPostScanTask).IsPublic);
        Assert.False(typeof(ArrTagsPostScanTask).IsAbstract);
    }

    [Fact]
    public void RegistratorWiresTheReconciliationTriggersWithoutStartupWork()
    {
        var services = new ServiceCollection();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(LibraryReconciliationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ArrTagsReconciliationTask));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ArrTagsPostScanTask));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMediaLibraryEnumerator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProviderConcurrencyLimiter));

        // Registration only adds descriptors; no task is instantiated and no
        // provider, rendering, or library work is performed.
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ArrTagsReconciliationTask)
                && descriptor.ImplementationInstance is not null);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ArrTagsPostScanTask)
                && descriptor.ImplementationInstance is not null);
    }

    [Fact]
    public void ReconciliationTriggersDoNotDependOnProviderOrPublicationTypes()
    {
        foreach (var type in new[]
        {
            typeof(LibraryReconciliationService),
            typeof(ArrTagsReconciliationTask),
            typeof(ArrTagsPostScanTask),
        })
        {
            foreach (var parameter in type.GetConstructors().SelectMany(constructor => constructor.GetParameters()))
            {
                var name = parameter.ParameterType.Name;
                Assert.DoesNotContain("Renderer", name, StringComparison.Ordinal);
                Assert.DoesNotContain("ArtworkPublisher", name, StringComparison.Ordinal);
                Assert.DoesNotContain("MetadataReconciliationProcessor", name, StringComparison.Ordinal);
                Assert.DoesNotContain("IArrProviderClient", name, StringComparison.Ordinal);
                Assert.DoesNotContain("Radarr", name, StringComparison.Ordinal);
                Assert.DoesNotContain("Sonarr", name, StringComparison.Ordinal);
            }
        }
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        public void Report(double value) => Values.Add(value);
    }

    private sealed class ReconciliationHarness : IDisposable
    {
        private readonly string _root;

        public ReconciliationHarness(
            int batchSize = 100,
            bool radarrEnabled = true,
            bool badgeMoviePosters = true,
            bool badgeEpisodePosters = false)
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-reconcile-trigger-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            Configuration = new ConfigurationSnapshotService(new PluginConfiguration
            {
                BadgeMoviePosters = badgeMoviePosters,
                BadgeEpisodePosters = badgeEpisodePosters,
                Radarr = new ArrConnectionConfiguration
                {
                    Enabled = radarrEnabled,
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
            Queue = new LibraryWorkQueue(() => Configuration.Current.Limits);

            Service = new LibraryReconciliationService(
                Configuration,
                Resolver,
                Enumerator,
                Sink,
                Fences);
        }

        public ConfigurationSnapshotService Configuration { get; }

        public StateRepository Repository { get; }

        public ArtworkLifecycleFenceStore Fences { get; }

        public ReconciliationLibraryResolver Resolver { get; }

        public FakeMediaLibraryEnumerator Enumerator { get; }

        public RecordingWorkHintSink Sink { get; }

        public LibraryWorkQueue Queue { get; }

        public LibraryReconciliationService Service { get; }

        public Guid AddMovie()
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Movie(id, ReconciliationFixtures.LibraryId);
            Resolver.LibraryIds[id] = ReconciliationFixtures.LibraryId;
            Resolver.Items[id] = item;
            Enumerator.Items.Add(item);
            return id;
        }

        public Guid AddRemoteMovie()
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Movie(id, ReconciliationFixtures.LibraryId);
            item.Location = LocationType.Remote;
            Resolver.LibraryIds[id] = ReconciliationFixtures.LibraryId;
            Resolver.Items[id] = item;
            Enumerator.Items.Add(item);
            return id;
        }

        public Guid AddSeriesOnly()
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Series(id, ReconciliationFixtures.LibraryId);
            Resolver.LibraryIds[id] = ReconciliationFixtures.LibraryId;
            Resolver.Items[id] = item;
            Enumerator.Items.Add(item);
            return id;
        }

        public Guid AddEpisode(Guid seriesId)
        {
            var series = ReconciliationFixtures.Series(seriesId, ReconciliationFixtures.LibraryId);
            Resolver.LibraryIds[seriesId] = ReconciliationFixtures.LibraryId;
            Resolver.Items[seriesId] = series;

            var id = Guid.NewGuid();
            var episode = ReconciliationFixtures.Episode(id, ReconciliationFixtures.LibraryId, seriesId);
            Resolver.LibraryIds[id] = ReconciliationFixtures.LibraryId;
            Resolver.Items[id] = episode;
            Enumerator.Items.Add(episode);
            return id;
        }

        public void Dispose()
        {
            Queue.Dispose();

            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// A bounded paged <see cref="IMediaLibraryEnumerator"/> double backed by an
/// in-memory item list. It records every requested page and invokes an optional
/// hook so a test can cancel or raise a fence during enumeration.
/// </summary>
internal sealed class FakeMediaLibraryEnumerator : IMediaLibraryEnumerator
{
    public List<BaseItem> Items { get; } = new();

    public List<(int Start, int Max)> Requests { get; } = new();

    public Action<int>? OnEnumerate { get; set; }

    public int CountCandidates() => Items.Count;

    public IReadOnlyList<BaseItem> EnumerateCandidates(int startIndex, int maxItems)
    {
        OnEnumerate?.Invoke(startIndex);

        if (startIndex < 0)
        {
            startIndex = 0;
        }

        if (maxItems < 1)
        {
            maxItems = 1;
        }

        Requests.Add((startIndex, maxItems));
        return Items.Skip(startIndex).Take(maxItems).ToArray();
    }
}
