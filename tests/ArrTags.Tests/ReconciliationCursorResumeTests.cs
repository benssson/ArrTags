using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Reconciliation;
using ArrTags.State;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 19.1 whole-scope reconciliation cursor behavior
/// (ADR-022 clauses 1, 2, 5, 6, and 7 as amended by ADR-029): the
/// identity-anchored resume, the unlocatable-anchor reset, the scope reset, the
/// cursor advance rule for the covered prefix, the wrap, and the trigger
/// exclusion that keeps the post-save prefix re-cover and the per-item triggers
/// away from the cursor. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class ReconciliationCursorResumeTests
{
    [Fact]
    public async Task ResumeWalksFromTheStartAndReturnsThePageStrictlyAfterTheAnchor()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(6);

        // The anchor is the third movie (absolute index 2), so the resume must
        // walk the host-ordered pages from the start and return the page that
        // begins at the first candidate strictly after it.
        harness.WriteCursor(items[2], "example movie");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(3, result.Inspected);
        Assert.Equal(3, result.Covered);
        Assert.Equal(3, result.Enqueued);
        Assert.Equal(new[] { items[3], items[4], items[5] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        // The locate walk is walked in bounded pages of the configured size.
        Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { (5, 2) }, harness.Enumerator.Requests);

        // Reaching the end of the order wraps the durable cursor to the start.
        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Wrapped, cursor.Status);
        Assert.False(cursor.HasPosition);
    }

    [Fact]
    public async Task MissingAndWrappedCursorsStartAtTheBeginningOfTheOrder()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(3);

        var missing = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, missing.Outcome);
        Assert.Empty(harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Enumerator.Requests);
        Assert.Equal(new[] { items[0], items[1], items[2] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        harness.WriteWrappedCursor();

        var wrapped = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.PostScan,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, wrapped.Outcome);
        Assert.Empty(harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { (0, 2), (2, 2), (0, 2), (2, 2) }, harness.Enumerator.Requests);
        Assert.Equal(6, harness.Sink.Hints.Count);
    }

    [Fact]
    public async Task UnlocatableAnchorResetsToTheStartAndRewritesTheCursor()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(3);

        // The recorded anchor is no longer in the candidate order (it was
        // removed), so the resume cannot locate it and the run resets to the
        // start of the order instead of failing.
        harness.WriteCursor(Guid.NewGuid(), "removed item");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Enumerator.Requests);
        Assert.Equal(new[] { items[0], items[1], items[2] }, harness.Sink.Hints.Select(hint => hint.ItemId));
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task ResumeLocatesTheAnchorByIdentityAndNotByTheSortNameHint()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(4);

        // The SortName component is a boundary hint and diagnostic only
        // (ADR-029 clause 3): a stale hint must not affect the locate step.
        harness.WriteCursor(items[1], "a stale sort name");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(new[] { (4, 2) }, harness.Enumerator.Requests);
        Assert.Equal(new[] { (0, 2) }, harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { items[2], items[3] }, harness.Sink.Hints.Select(hint => hint.ItemId));
        Assert.Equal(2, result.Covered);
    }

    [Theory]
    [InlineData("library-added")]
    [InlineData("library-removed")]
    [InlineData("movie-scope")]
    [InlineData("episode-scope")]
    public async Task ScopeChangeResetsToTheStart(string change)
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(3);

        var recordedSnapshot = change switch
        {
            "library-added" => harness.Snapshot(libraries: new[] { harness.LibraryId.ToString(), "added-library" }),
            "library-removed" => harness.Snapshot(libraries: Array.Empty<string>()),
            "movie-scope" => harness.Snapshot(moviePosters: false),
            "episode-scope" => harness.Snapshot(episodePosters: true),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        harness.WriteCursorFor(ReconciliationScopeIdentity.From(recordedSnapshot), items[1], "example movie");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Empty(harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { items[0], items[1], items[2] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        // The run rewrites the record for the current scope.
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task TornCursorRecordResetsToTheStartAndIsRebuilt()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(3);
        harness.WriteCursor(items[1], "example movie");

        var path = harness.CursorPath();
        await File.WriteAllTextAsync(path, "{\"SchemaVersion\":1,\"PayloadJson\":\"torn");

        Assert.Equal(ReconciliationCursorStatus.Discarded, harness.ReadCursor().Status);

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Empty(harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { items[0], items[1], items[2] }, harness.Sink.Hints.Select(hint => hint.ItemId));
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task OverflowStopsAtTheFirstUncoveredItemAndAdvancesTheCursorOnlyOverTheCoveredPrefix()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(5);
        harness.Sink.OutcomeOverride = hint =>
            hint.ItemId == items[2] ? WorkHintEnqueueOutcome.Overflow : WorkHintEnqueueOutcome.Accepted;

        var first = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, first.Outcome);
        Assert.Equal(3, first.Inspected);
        Assert.Equal(2, first.Covered);
        Assert.Equal(2, first.Enqueued);
        Assert.Equal(new[] { items[0], items[1] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        // The cursor is the last covered item, not the overflowed item.
        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[1], cursor.Position!.Value.ItemId);
        Assert.Equal("example movie", cursor.Position!.Value.SortName);

        // The next run resumes at the first uncovered (overflowed) item.
        harness.Sink.OutcomeOverride = null;
        var second = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(new[] { items[2], items[3], items[4] }, harness.Sink.Hints.Skip(2).Select(hint => hint.ItemId));
        Assert.Equal(LibraryReconciliationOutcome.Completed, second.Outcome);
        Assert.Equal(new[] { (0, 2) }, harness.Enumerator.ResumeRequests);
    }

    [Fact]
    public async Task StoppedStopsAtTheFirstUncoveredItemAndAdvancesTheCursorOnlyOverTheCoveredPrefix()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(5);
        harness.Sink.OutcomeOverride = hint =>
            hint.ItemId == items[2] ? WorkHintEnqueueOutcome.Stopped : WorkHintEnqueueOutcome.Accepted;

        var first = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // ADR-022 clause 2: a stopped enqueue (the queue refuses new work, for
        // example on host shutdown) ends the whole-scope run exactly like an
        // overflow, so the covered prefix is the accepted prefix. The stopped
        // item is not covered and is not counted in the covered prefix.
        Assert.Equal(LibraryReconciliationOutcome.Fenced, first.Outcome);
        Assert.Equal(3, first.Inspected);
        Assert.Equal(2, first.Covered);
        Assert.Equal(2, first.Enqueued);
        Assert.Equal(new[] { items[0], items[1] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        // The cursor is the last covered item, not the stopped item.
        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[1], cursor.Position!.Value.ItemId);
        Assert.Equal("example movie", cursor.Position!.Value.SortName);

        // The next run resumes at the first uncovered (stopped) item.
        harness.Sink.OutcomeOverride = null;
        var second = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(new[] { items[2], items[3], items[4] }, harness.Sink.Hints.Skip(2).Select(hint => hint.ItemId));
        Assert.Equal(LibraryReconciliationOutcome.Completed, second.Outcome);
        Assert.Equal(new[] { (0, 2) }, harness.Enumerator.ResumeRequests);
    }

    [Fact]
    public async Task CoalescedAndInFlightOutcomesAreCoveredAndDoNotStallTheRun()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(3);
        harness.Sink.OutcomeOverride = hint => hint.ItemId switch
        {
            var id when id == items[0] => WorkHintEnqueueOutcome.Coalesced,
            var id when id == items[1] => WorkHintEnqueueOutcome.InFlight,
            _ => WorkHintEnqueueOutcome.Accepted,
        };

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(3, result.Inspected);
        Assert.Equal(3, result.Covered);
        Assert.Equal(1, result.Enqueued);
        Assert.Equal(new[] { items[2] }, harness.Sink.Hints.Select(hint => hint.ItemId));
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task AnItemThatNeedsNoWorkIsCoveredAndDoesNotStallTheCursor()
    {
        using var harness = new CursorHarness(batchSize: 1);
        var first = harness.AddMovie();
        harness.AddRemoteMovie();
        var third = harness.AddMovie();
        var fourth = harness.AddMovie();
        harness.Sink.OutcomeOverride = hint =>
            hint.ItemId == fourth ? WorkHintEnqueueOutcome.Overflow : WorkHintEnqueueOutcome.Accepted;

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, result.Outcome);
        Assert.Equal(4, result.Inspected);
        Assert.Equal(3, result.Eligible);
        Assert.Equal(3, result.Covered);
        Assert.Equal(new[] { first, third }, harness.Sink.Hints.Select(hint => hint.ItemId));

        // The ineligible item is covered, so the cursor is the last covered
        // eligible item and not the first item of the run.
        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(third, cursor.Position!.Value.ItemId);
    }

    [Fact]
    public async Task CancellationAdvancesTheCursorOnlyOverTheCoveredPrefix()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(4);

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
        Assert.Equal(2, result.Inspected);
        Assert.Equal(2, result.Covered);
        Assert.Equal(new[] { items[0], items[1] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[1], cursor.Position!.Value.ItemId);
    }

    [Fact]
    public async Task FenceStopAdvancesTheCursorOnlyOverTheCoveredPrefix()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(4);

        harness.Enumerator.OnEnumerate = start =>
        {
            if (start == 0)
            {
                harness.Fences.Set(ArtworkLifecycleFence.Disable, "test disable");
            }
        };

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Fenced, result.Outcome);
        Assert.Equal(2, result.Inspected);
        Assert.Equal(2, result.Covered);
        Assert.Equal(new[] { items[0], items[1] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[1], cursor.Position!.Value.ItemId);
    }

    [Fact]
    public async Task ResumeCancellationIsReportedAsACancelledRun()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(4);
        harness.WriteCursor(items[3], "example movie");

        using var cts = new CancellationTokenSource();
        harness.Enumerator.OnResume = _ =>
        {
#pragma warning disable CA1849 // A synchronous enumeration hook cannot await CancelAsync.
            cts.Cancel();
#pragma warning restore CA1849
        };

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            cts.Token);

        Assert.Equal(LibraryReconciliationOutcome.Cancelled, result.Outcome);
        Assert.Empty(harness.Enumerator.Requests);
        Assert.Empty(harness.Sink.Hints);

        // Nothing was covered, so the recorded cursor is left untouched.
        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[3], cursor.Position!.Value.ItemId);
    }

    [Fact]
    public async Task PostSaveEnumeratesFromTheStartAndLeavesTheCursorUntouched()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(3);
        harness.WriteCursor(items[0], "example movie");

        var path = harness.CursorPath();
        var before = await File.ReadAllBytesAsync(path);

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.PostSave,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Empty(harness.Enumerator.ResumeRequests);
        Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Enumerator.Requests);
        Assert.Equal(new[] { items[0], items[1], items[2] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        // The post-save trigger is a bounded prefix re-cover under the just
        // activated configuration: it never reads or writes the cursor, so the
        // record is byte-identical.
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task PostSaveDoesNotStopAtOverflowWhileTheCursorRunDoes()
    {
        using var harness = new CursorHarness(batchSize: 1);
        var items = harness.AddMovies(3);
        harness.Sink.OutcomeOverride = hint =>
            hint.ItemId == items[1] ? WorkHintEnqueueOutcome.Overflow : WorkHintEnqueueOutcome.Accepted;

        // The bounded post-save prefix re-cover keeps enumerating exactly as
        // before (ADR-022 clause 6): the queue bound still limits the work, but
        // the run does not stop at the overflowed item.
        var postSave = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.PostSave,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, postSave.Outcome);
        Assert.Equal(3, postSave.Inspected);
        Assert.Equal(2, postSave.Enqueued);
        Assert.Equal(new[] { items[0], items[2] }, harness.Sink.Hints.Select(hint => hint.ItemId));

        // The scheduled whole-scope run stops at the first overflow.
        var scheduled = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, scheduled.Outcome);
        Assert.Equal(2, scheduled.Inspected);
        Assert.Equal(1, scheduled.Covered);
        Assert.Equal(1, scheduled.Enqueued);
    }

    [Fact]
    public async Task SuccessiveRunsAdvanceThroughTheOrderAndWrapWithTheRealQueue()
    {
        using var harness = new CursorHarness(batchSize: 2);
        var items = harness.AddMovies(6);

        // The real bounded queue: two pending items saturate it and stop the run.
        using var queue = new LibraryWorkQueue(2);
        var service = new LibraryReconciliationService(
            harness.Configuration,
            harness.Resolver,
            harness.Enumerator,
            queue,
            harness.Fences,
            harness.Cursor);

        var first = await service.ReconcileAsync(LibraryReconciliationSource.Scheduled, null, CancellationToken.None);
        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, first.Outcome);
        Assert.Equal(2, first.Covered);
        Assert.Equal(items[1], harness.ReadCursor().Position!.Value.ItemId);
        Assert.Equal(2, queue.Count);
        Drain(queue);

        var second = await service.ReconcileAsync(LibraryReconciliationSource.Scheduled, null, CancellationToken.None);
        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, second.Outcome);
        Assert.Equal(2, second.Covered);
        Assert.Equal(items[3], harness.ReadCursor().Position!.Value.ItemId);
        Assert.Equal(2, queue.Count);
        Drain(queue);

        var third = await service.ReconcileAsync(LibraryReconciliationSource.PostScan, null, CancellationToken.None);
        Assert.Equal(LibraryReconciliationOutcome.Completed, third.Outcome);
        Assert.Equal(2, third.Covered);
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
        Drain(queue);

        // A wrapped cursor restarts the round-robin at the first item.
        var fourth = await service.ReconcileAsync(LibraryReconciliationSource.Scheduled, null, CancellationToken.None);
        Assert.Equal(2, fourth.Covered);
        Assert.Equal(items[1], harness.ReadCursor().Position!.Value.ItemId);
    }

    [Fact]
    public void RegistratorWiresThePersistedCursorStore()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ReconciliationCursorStore));
    }

    [Fact]
    public void PerItemTriggersAndThePostSaveTriggerDoNotReferenceTheCursorStore()
    {
        // The cursor governs only the whole-scope scheduled/post-scan triggers:
        // the library-event source, the webhook path, the bounded post-save
        // trigger, and the scheduled/post-scan task wrappers must not depend on
        // the cursor store.
        foreach (var type in new[]
        {
            typeof(ArrTags.PluginLifecycle.ArrTagsLifecycleService),
            typeof(ArrTags.PluginLifecycle.JellyfinLibraryEventSource),
            typeof(ArrTags.PluginLifecycle.ConfigurationReconciliationTrigger),
            typeof(ArrTags.PluginLifecycle.ArrTagsReconciliationTask),
            typeof(ArrTags.PluginLifecycle.ArrTagsPostScanTask),
            typeof(ArrTags.Webhooks.WebhookIntakeService),
            typeof(ArrTags.Webhooks.WebhookReconciliationResolver),
            typeof(ArrTags.Updates.LibraryWorkQueue),
        })
        {
            foreach (var parameter in type.GetConstructors().SelectMany(constructor => constructor.GetParameters()))
            {
                Assert.DoesNotContain("ReconciliationCursor", parameter.ParameterType.Name, StringComparison.Ordinal);
            }

            foreach (var field in type.GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public))
            {
                Assert.DoesNotContain("ReconciliationCursor", field.FieldType.Name, StringComparison.Ordinal);
            }
        }
    }

    private static void Drain(LibraryWorkQueue queue)
    {
        while (queue.Count > 0)
        {
            var item = queue.DequeueAsync(CancellationToken.None).GetAwaiter().GetResult();
            queue.CompleteProcessing(item.Key);
        }
    }

    /// <summary>
    /// A harness for the real bounded reconciliation service over an in-memory
    /// library, a real temporary state repository, and a recording work hint
    /// sink whose outcome can be scripted.
    /// </summary>
    private sealed class CursorHarness : IDisposable
    {
        private readonly string _root;

        public CursorHarness(int batchSize = 2)
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-cursor-resume-" + Guid.NewGuid().ToString("N"));
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
            Cursor = new ReconciliationCursorStore(Repository);
            Resolver = new ReconciliationLibraryResolver();
            Enumerator = new FakeMediaLibraryEnumerator();
            Sink = new RecordingWorkHintSink();

            Service = new LibraryReconciliationService(
                Configuration,
                Resolver,
                Enumerator,
                Sink,
                Fences,
                Cursor);
        }

        public ConfigurationSnapshotService Configuration { get; }

        public StateRepository Repository { get; }

        public ArtworkLifecycleFenceStore Fences { get; }

        public ReconciliationCursorStore Cursor { get; }

        public ReconciliationLibraryResolver Resolver { get; }

        public FakeMediaLibraryEnumerator Enumerator { get; }

        public RecordingWorkHintSink Sink { get; }

        public LibraryReconciliationService Service { get; }

        public Guid LibraryId => ReconciliationFixtures.LibraryId;

        public IReadOnlyList<Guid> AddMovies(int count)
        {
            var items = new List<Guid>(count);
            for (var index = 0; index < count; index++)
            {
                items.Add(AddMovie());
            }

            return items;
        }

        public Guid AddMovie()
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Movie(id, LibraryId);
            Resolver.LibraryIds[id] = LibraryId;
            Resolver.Items[id] = item;
            Enumerator.Items.Add(item);
            return id;
        }

        public Guid AddRemoteMovie()
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Movie(id, LibraryId);
            item.Location = MediaBrowser.Model.Entities.LocationType.Remote;
            Resolver.LibraryIds[id] = LibraryId;
            Resolver.Items[id] = item;
            Enumerator.Items.Add(item);
            return id;
        }

        public ReconciliationCursorReadResult ReadCursor()
        {
            return Cursor.Read(ReconciliationScopeIdentity.From(Configuration.Current));
        }

        public void WriteCursor(Guid itemId, string sortName)
        {
            WriteCursorFor(ReconciliationScopeIdentity.From(Configuration.Current), itemId, sortName);
        }

        public void WriteCursorFor(ReconciliationScopeIdentity scope, Guid itemId, string sortName)
        {
            Cursor.Write(scope, new ReconciliationCursorPosition(sortName, itemId));
        }

        public void WriteWrappedCursor()
        {
            Cursor.Write(ReconciliationScopeIdentity.From(Configuration.Current), position: null);
        }

        public string CursorPath()
        {
            return Repository.Paths.GetRecordPath(
                StateAuthority.Cache,
                ReconciliationCursorStore.RecordKind,
                ReconciliationCursorStore.RecordId);
        }

        public PluginConfigurationSnapshot Snapshot(
            IReadOnlyList<string>? libraries = null,
            bool moviePosters = true,
            bool episodePosters = false)
        {
            var configuration = new PluginConfiguration
            {
                BadgeMoviePosters = moviePosters,
                BadgeEpisodePosters = episodePosters,
            };

            if (libraries is not null)
            {
                foreach (var library in libraries)
                {
                    configuration.EnabledLibraries.Add(library);
                }
            }
            else
            {
                configuration.EnabledLibraries.Add(LibraryId.ToString());
            }

            return PluginConfigurationSnapshot.From(configuration);
        }

        public void Dispose()
        {
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
