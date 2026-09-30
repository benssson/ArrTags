using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.State;
using ArrTags.Updates;
using ArrTags.Webhooks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// The task 19.2 F4 successive-run coverage matrix (ADR-022 as amended by
/// ADR-029). Every fact drives the real
/// <see cref="JellyfinMediaLibraryEnumerator"/> over an <see cref="ILibraryManager"/>
/// double that serves the pinned host's effective <c>(SortName, Name)</c>
/// candidate order, the real <see cref="LibraryReconciliationService"/>, a real
/// <see cref="ReconciliationCursorStore"/> under a temporary
/// <see cref="StateRepository"/>, and the real <see cref="LibraryWorkQueue"/>
/// (wrapped only to record the bounded enqueue outcome the queue itself
/// returned). The facts cover the identity-anchor resume (strictly after the
/// located boundary item, with the <c>SortName</c> component as a
/// hint/diagnostic), the unlocatable anchor, library mutation before the anchor,
/// round-robin coverage of a scope larger than <c>QueueCapacity</c>, the
/// coalesce/in-flight/overflow-stop/stopped cases, the scope reset, trigger
/// exclusion, the page-reach/offset bound, and the registered exact-tie
/// residual. No live Jellyfin or Arr instance is required.
/// </summary>
/// <remarks>
/// Matrix map (task verification requirement -> facts):
/// 1 identity-anchor resume -> IdentityAnchorResumeReturnsThePageStrictlyAfterTheAnchorAndTreatsSortNameAsAHint,
///   ResumeUsesTheHostSortNameThenNameOrderAndNotTheInsertionOrder;
/// 2 unlocatable anchor -> UnlocatableAnchorResetsToTheStartAndTheRunDoesNotFail;
/// 3 mutation before the anchor -> LibraryMutationBeforeTheAnchorDoesNotShiftTheResumePoint;
/// 4 scope larger than QueueCapacity -> SuccessiveRunsCoverAScopeLargerThanQueueCapacityRoundRobinAndWrap;
/// 5 coalesce -> CoalescedEnqueueIsCoveredAndDoesNotStallTheRun;
/// 6 in-flight -> InFlightEnqueueIsCoveredAndDoesNotStallTheRun;
/// 7 overflow-stop -> OverflowStopsAtTheFirstUncoveredItemAndTheNextRunResumesThere;
/// 8 stopped -> StoppedQueueStopsAtTheFirstUncoveredItemAndTheNextRunResumesThere;
/// 9 scope reset -> ChangedScopeTupleResetsTheCursorToTheStart;
/// 10 trigger exclusion -> PostSaveTriggerEnumeratesFromTheStartAndLeavesTheCursorUntouched,
///   LibraryEventAndPerItemPathsEnqueueTheirHintAndLeaveTheCursorUntouched,
///   WebhookPathEnqueuesItsResolvedHintAndLeavesTheCursorUntouched,
///   OnlyWholeScopeTriggersReferenceTheCursorStore;
/// 11 page-reach bound -> OneRunPageReachTracksTheServerWideCandidateCountPlusQueueCapacity,
///   ResumeCancellationInsideTheRealLocateWalkLeavesTheCursorUntouched;
/// 12 tie caveat -> ExactSortNameNameTiesAreBestEffortAndNotCoveredWithinTheCycle.
/// </remarks>
public sealed class SuccessiveRunCoverageMatrixTests
{
    [Fact]
    public async Task IdentityAnchorResumeReturnsThePageStrictlyAfterTheAnchorAndTreatsSortNameAsAHint()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 2);
        var items = harness.AddMovies(8);

        // A deliberately stale SortName hint: ADR-029 clause 3 locates the unique
        // itemId in the host order, so the hint must not influence the resume.
        harness.WriteCursor(items[2], "zzz-stale-hint");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, result.Outcome);
        Assert.Equal(3, result.Inspected);
        Assert.Equal(2, result.Covered);

        // Strictly after the boundary item (m04), not the page after its page
        // (m05): a page-aligned resume would have covered only m05 before the
        // queue filled.
        Assert.Equal(new[] { items[3], items[4] }, harness.AcceptedItems());

        // The persisted position is the last covered item with that item's own
        // SortName recorded as a diagnostic hint.
        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[4], cursor.Position!.Value.ItemId);
        Assert.Equal("m05", cursor.Position!.Value.SortName);

        // The locate walk read bounded host pages from the start of the order and
        // then resumed at the anchor's absolute index + 1.
        Assert.Equal(new[] { (0, 2), (2, 2), (3, 2), (5, 2) }, harness.Requests);
    }

    [Fact]
    public async Task ResumeUsesTheHostSortNameThenNameOrderAndNotTheInsertionOrder()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);

        harness.AddMovie("alpha");
        var betaB = harness.AddMovie("beta", "Beta B");
        var betaA = harness.AddMovie("beta", "Beta A");
        var gamma = harness.AddMovie("gamma");

        // The host breaks the shared SortName by the raw Name, so the host order
        // is alpha, Beta A, Beta B, gamma even though Beta B was added first; the
        // cursor on Beta A must resume at Beta B and then gamma.
        harness.WriteCursor(betaA, "beta");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(new[] { betaB, gamma }, harness.AcceptedItems());
        Assert.Equal(new[] { (0, 2), (2, 2), (4, 2) }, harness.Requests);
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("kind-changed")]
    public async Task UnlocatableAnchorResetsToTheStartAndTheRunDoesNotFail(string scenario)
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(5);

        // "removed": the recorded item is gone from the library. "kind-changed":
        // the item left the movie/episode candidate enumeration (the query only
        // returns Movie and Episode), so it can no longer be located either.
        var anchor = scenario == "removed"
            ? Guid.NewGuid()
            : harness.AddSeries("m03");

        harness.WriteCursor(anchor, "m03");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // ADR-029 clause 4: an anchor that cannot be located resets the run to
        // the start rather than failing it. The locate walk covered the whole
        // order, then the run covered the whole scope from the start.
        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(5, result.Covered);
        Assert.Equal(items, harness.AcceptedItems());
        Assert.Equal(
            new[] { (0, 2), (2, 2), (4, 2), (0, 2), (2, 2), (4, 2) },
            harness.Requests);
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task LibraryMutationBeforeTheAnchorDoesNotShiftTheResumePoint()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var original = harness.AddMovies(7);
        harness.WriteCursor(original[3], "m04");

        // Between runs: one item before the cursor is removed, one is added
        // before the cursor, and one is added after the cursor.
        harness.RemoveItem(original[1]);
        var addedBefore = harness.AddMovie("m03a");
        var addedAfter = harness.AddMovie("m05a");

        var first = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // The identity anchor keeps the resume point on m04 even though its
        // absolute index shifted: the item added after the cursor is covered in
        // this cycle and the item added before it is not.
        Assert.Equal(LibraryReconciliationOutcome.Completed, first.Outcome);
        Assert.Equal(
            new[] { original[4], addedAfter, original[5], original[6] },
            harness.AcceptedItems());
        Assert.DoesNotContain(addedBefore, harness.AcceptedItems());
        Assert.Equal(new[] { (0, 2), (2, 2), (4, 2), (6, 2), (8, 2) }, harness.Requests.Take(5));
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);

        // The next run wraps to the start and covers the item added before the
        // old cursor (ADR-022 clause 3 best-effort mutation rule).
        harness.DrainAndClearOutcomes();
        var second = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.PostScan,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, second.Outcome);
        Assert.Equal(
            new[]
            {
                original[0], original[2], addedBefore, original[3],
                original[4], addedAfter, original[5], original[6],
            },
            harness.AcceptedItems());
    }

    [Fact]
    public async Task SuccessiveRunsCoverAScopeLargerThanQueueCapacityRoundRobinAndWrap()
    {
        const int capacity = 3;
        const int scopeSize = 14;
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: capacity);
        var items = harness.AddMovies(scopeSize);

        var coveredOrder = new List<Guid>();
        var perRunCovered = new List<int>();

        while (coveredOrder.Count < scopeSize)
        {
            var rowsBefore = harness.RowsServed;
            var result = await harness.Service.ReconcileAsync(
                LibraryReconciliationSource.Scheduled,
                progress: null,
                CancellationToken.None);

            perRunCovered.Add(result.Covered);
            coveredOrder.AddRange(harness.DrainAndClearOutcomes());

            // One run stays bounded: it covers at most the queue capacity and its
            // page reach stays within the candidate count plus the queue capacity
            // (ADR-029 clause 3), not a constant.
            Assert.InRange(result.Covered, 1, capacity);
            Assert.True(
                harness.RowsServed - rowsBefore <= scopeSize + capacity,
                $"A single run read {harness.RowsServed - rowsBefore} rows, above {scopeSize} + {capacity}.");
        }

        // Round-robin: every item is covered exactly once, in host order, before
        // the wrap; the prefix is never re-covered.
        Assert.Equal(items, coveredOrder);
        Assert.Equal(new[] { 3, 3, 3, 3, 2 }, perRunCovered);
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);

        // A wrapped cursor restarts the cycle at the first candidate.
        var restart = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, restart.Outcome);
        Assert.Equal(new[] { items[0], items[1], items[2] }, harness.DrainAndClearOutcomes());
    }

    [Fact]
    public async Task CoalescedEnqueueIsCoveredAndDoesNotStallTheRun()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(6);

        // A pending work item with the same version-blind key (item, unresolved
        // connection, Primary surface) makes the real queue coalesce the run's
        // hint for that item.
        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            harness.Queue.Enqueue(harness.Hint(items[0])));

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // ADR-022 clause 2: a coalesced item is covered and the run continues.
        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(6, result.Covered);
        Assert.Equal(5, result.Enqueued);
        Assert.Equal(new[] { WorkHintEnqueueOutcome.Coalesced }, harness.OutcomesFor(items[0]));
        Assert.Equal(
            new[] { items[1], items[2], items[3], items[4], items[5] },
            harness.AcceptedItems());
        Assert.Equal(6, harness.Queue.Count);
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task InFlightEnqueueIsCoveredAndDoesNotStallTheRun()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(6);

        // A dequeued (in-flight) work item with the same key makes the real queue
        // report InFlight for the run's hint; the single-flight slot is held.
        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            harness.Queue.Enqueue(harness.Hint(items[0])));
        var inFlight = await harness.Queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(items[0], inFlight.Key.ItemId);

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // ADR-022 clause 2: an in-flight item is covered and the run continues.
        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(6, result.Covered);
        Assert.Equal(5, result.Enqueued);
        Assert.Equal(new[] { WorkHintEnqueueOutcome.InFlight }, harness.OutcomesFor(items[0]));
        Assert.Equal(
            new[] { items[1], items[2], items[3], items[4], items[5] },
            harness.AcceptedItems());
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);

        harness.Queue.CompleteProcessing(inFlight.Key);
    }

    [Fact]
    public async Task OverflowStopsAtTheFirstUncoveredItemAndTheNextRunResumesThere()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 2);
        var items = harness.AddMovies(4);

        var first = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // The real queue accepts two items and reports Overflow for the third, so
        // the run stops there and the covered prefix is the accepted prefix.
        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, first.Outcome);
        Assert.Equal(3, first.Inspected);
        Assert.Equal(2, first.Covered);
        Assert.Equal(2, first.Enqueued);
        Assert.Equal(new[] { items[0], items[1] }, harness.AcceptedItems());
        Assert.Equal(new[] { WorkHintEnqueueOutcome.Overflow }, harness.OutcomesFor(items[2]));

        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[1], cursor.Position!.Value.ItemId);
        Assert.Equal("m02", cursor.Position!.Value.SortName);

        var firstRunRequests = harness.Requests.Count;
        harness.DrainAndClearOutcomes();

        // The next run resumes at the first uncovered (overflowed) item; the
        // already covered prefix is not re-enqueued.
        var second = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.PostScan,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, second.Outcome);
        Assert.Equal(new[] { items[2], items[3] }, harness.AcceptedItems());
        Assert.Equal(
            new[] { (0, 2), (2, 2), (4, 2) },
            harness.Requests.Skip(firstRunRequests));
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task StoppedQueueStopsAtTheFirstUncoveredItemAndTheNextRunResumesThere()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 32);
        var items = harness.AddMovies(6);

        // Stop the real queue while the run is between pages: the run accepts the
        // first page and then the real queue reports Stopped for the next item.
        harness.Manager.OnRequest = start =>
        {
            if (start == 2)
            {
                harness.Manager.OnRequest = null;
                harness.Queue.StopAccepting();
            }
        };

        var first = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // ADR-022 clause 2: a Stopped outcome stops the whole-scope run exactly
        // like an overflow, so the covered prefix is the accepted prefix.
        Assert.Equal(LibraryReconciliationOutcome.Fenced, first.Outcome);
        Assert.Equal(3, first.Inspected);
        Assert.Equal(2, first.Covered);
        Assert.Equal(2, first.Enqueued);
        Assert.Equal(new[] { items[0], items[1] }, harness.AcceptedItems());
        Assert.Equal(new[] { WorkHintEnqueueOutcome.Stopped }, harness.OutcomesFor(items[2]));

        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[1], cursor.Position!.Value.ItemId);

        harness.DrainAndClearOutcomes();
        harness.Queue.StartAccepting();

        // The next run resumes at the first uncovered (stopped) item.
        var second = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, second.Outcome);
        Assert.Equal(
            new[] { items[2], items[3], items[4], items[5] },
            harness.AcceptedItems());
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Theory]
    [InlineData("enabled-library-set")]
    [InlineData("item-type-scope")]
    public async Task ChangedScopeTupleResetsTheCursorToTheStart(string change)
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(5);

        var recordedScope = change switch
        {
            "enabled-library-set" => ReconciliationScopeIdentity.From(
                PluginConfigurationSnapshot.From(WithLibraries("other-library"))),
            "item-type-scope" => ReconciliationScopeIdentity.From(
                PluginConfigurationSnapshot.From(new PluginConfiguration { BadgeMoviePosters = false })),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        harness.WriteCursorFor(recordedScope, items[2], "m03");

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        // ADR-022 clause 5: a changed scope tuple resets to the start rather than
        // resuming after m03; no locate walk is performed and the whole current
        // scope is covered.
        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(items, harness.AcceptedItems());
        Assert.Equal(new[] { (0, 2), (2, 2), (4, 2) }, harness.Requests);
        Assert.Equal(ReconciliationCursorStatus.Wrapped, harness.ReadCursor().Status);
    }

    [Fact]
    public async Task PostSaveTriggerEnumeratesFromTheStartAndLeavesTheCursorUntouched()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(3);
        harness.WriteCursor(items[0], "m01");

        var before = await File.ReadAllBytesAsync(harness.CursorPath());

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.PostSave,
            progress: null,
            CancellationToken.None);

        // ADR-022 clause 6: the bounded post-save prefix re-cover always
        // enumerates from the start (no locate walk) and never reads or writes
        // the cursor, so the record is byte-identical.
        Assert.Equal(LibraryReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(items, harness.AcceptedItems());
        Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Requests);
        Assert.Equal(before, await File.ReadAllBytesAsync(harness.CursorPath()));
    }

    [Fact]
    public async Task LibraryEventAndPerItemPathsEnqueueTheirHintAndLeaveTheCursorUntouched()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(2);
        harness.WriteCursor(items[0], "m01");

        var before = await File.ReadAllBytesAsync(harness.CursorPath());

        var events = new FakeLibraryEventSource();
        var lifecycle = new ArrTagsLifecycleService(
            events,
            new NoOpLifecycleCoordinator(),
            harness.Configuration,
            harness.Queue);
        await lifecycle.StartAsync(CancellationToken.None);

        var added = Guid.NewGuid();
        var updated = Guid.NewGuid();
        events.RaiseAdded(LibraryEventFixtures.Change(added, LibraryWorkReason.Added, MediaItemType.Movie));
        events.RaiseUpdated(LibraryEventFixtures.Change(updated, LibraryWorkReason.Updated, MediaItemType.Movie));

        // The per-item triggers enqueue their own bounded hints and never run the
        // whole-scope enumeration, so the cursor is not read or written.
        Assert.Equal(2, harness.Queue.Count);
        Assert.Empty(harness.Requests);
        Assert.Equal(before, await File.ReadAllBytesAsync(harness.CursorPath()));

        await lifecycle.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WebhookPathEnqueuesItsResolvedHintAndLeavesTheCursorUntouched()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(2);
        harness.WriteCursor(items[0], "m01");

        var before = await File.ReadAllBytesAsync(harness.CursorPath());

        var store = new MetadataStateStore(harness.Repository);
        store.Write(ReconciliationFixtures.MatchedEntry());
        using var intake = new WebhookIntake(8);
        using var webhook = new WebhookIntakeService(
            intake,
            new WebhookReconciliationResolver(store),
            harness.Queue,
            harness.Configuration);

        await webhook.StartAsync(CancellationToken.None);
        Assert.True(intake.TrySubmit(new WebhookEvent(
            ArrProviderKind.Radarr,
            WebhookEventType.Download,
            movieId: 42)));

        Assert.True(
            await WaitUntilAsync(() => harness.Queue.Count == 1),
            "The webhook intake did not enqueue the resolved hint within the bounded wait.");

        var queued = await harness.Queue.DequeueAsync(CancellationToken.None);
        harness.Queue.CompleteProcessing(queued.Key);

        // The webhook trigger enqueues its per-item hint and never runs the
        // whole-scope enumeration or touches the cursor.
        Assert.Equal(ReconciliationFixtures.ItemId, queued.Key.ItemId);
        Assert.Empty(harness.Requests);
        Assert.Equal(before, await File.ReadAllBytesAsync(harness.CursorPath()));

        await webhook.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void OnlyWholeScopeTriggersReferenceTheCursorStore()
    {
        // Positive control: the whole-scope service does take the persisted
        // cursor, so the absence check below is not vacuous.
        Assert.Contains(
            typeof(LibraryReconciliationService)
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == typeof(ReconciliationCursorStore));

        foreach (var type in new[]
        {
            typeof(ArrTagsLifecycleService),
            typeof(JellyfinLibraryEventSource),
            typeof(ConfigurationReconciliationTrigger),
            typeof(ArrTagsReconciliationTask),
            typeof(ArrTagsPostScanTask),
            typeof(WebhookIntakeService),
            typeof(WebhookReconciliationResolver),
            typeof(LibraryWorkQueue),
        })
        {
            foreach (var parameter in type
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()))
            {
                Assert.DoesNotContain(
                    "ReconciliationCursor",
                    parameter.ParameterType.Name,
                    StringComparison.Ordinal);
            }

            foreach (var field in type.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Assert.DoesNotContain(
                    "ReconciliationCursor",
                    field.FieldType.Name,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task OneRunPageReachTracksTheServerWideCandidateCountPlusQueueCapacity()
    {
        const int capacity = 3;
        const int batchSize = 4;

        var small = await MeasureRunReachAsync(scopeSize: 8, batchSize, capacity);
        var large = await MeasureRunReachAsync(scopeSize: 16, batchSize, capacity);

        // The reach grows with the candidate count: the bound is the scope size
        // plus QueueCapacity (ADR-029 clause 3), not a constant page count.
        Assert.True(
            large.RowsServed > small.RowsServed,
            $"Expected the larger scope to read more rows; small={small.RowsServed}, large={large.RowsServed}.");
        Assert.True(
            small.RowsServed <= 8 + capacity,
            $"The 8-candidate run read {small.RowsServed} rows, above 8 + {capacity}.");
        Assert.True(
            large.RowsServed <= 16 + capacity,
            $"The 16-candidate run read {large.RowsServed} rows, above 16 + {capacity}.");

        // Both runs read more rows than the queue capacity, so the reach is
        // driven by the resume offset and not by a constant bound.
        Assert.True(small.RowsServed > capacity);
        Assert.True(large.RowsServed > capacity);

        // The locate walk is always walked in bounded ReconciliationBatchSize pages.
        Assert.All(small.Requests, request => Assert.Equal(batchSize, request.Max));
        Assert.All(large.Requests, request => Assert.Equal(batchSize, request.Max));

        // One run still performs bounded enqueue work.
        Assert.InRange(small.Covered, 1, capacity);
        Assert.InRange(large.Covered, 1, capacity);
    }

    [Fact]
    public async Task ResumeCancellationInsideTheRealLocateWalkLeavesTheCursorUntouched()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 64);
        var items = harness.AddMovies(6);
        harness.WriteCursor(items[4], "m05");

        using var cts = new CancellationTokenSource();
        harness.Manager.OnRequest = start =>
        {
            if (start == 2)
            {
#pragma warning disable CA1849 // A synchronous query hook cannot await CancelAsync.
                cts.Cancel();
#pragma warning restore CA1849
            }
        };

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            cts.Token);

        // The real locate walk checks cancellation inside each page (ADR-022
        // clause 7), so the run reports the bounded cancelled result, covers
        // nothing, and leaves the recorded cursor untouched.
        Assert.Equal(LibraryReconciliationOutcome.Cancelled, result.Outcome);
        Assert.Empty(harness.AcceptedItems());
        Assert.Equal(new[] { (0, 2), (2, 2) }, harness.Requests);

        var cursor = harness.ReadCursor();
        Assert.Equal(ReconciliationCursorStatus.Position, cursor.Status);
        Assert.Equal(items[4], cursor.Position!.Value.ItemId);
    }

    [Fact]
    public async Task ExactSortNameNameTiesAreBestEffortAndNotCoveredWithinTheCycle()
    {
        using var harness = new CoverageHarness(batchSize: 2, queueCapacity: 2);

        var before = harness.AddMovie("a-before");
        var first = harness.AddMovie("tie", "Same");
        var second = harness.AddMovie("tie", "Same");
        var after = harness.AddMovie("z-after");

        // The two rows tie on the exact (SortName, Name) pair, so the host has no
        // guaranteed relative order between them; the double keeps their
        // insertion order as the unspecified tie-break.
        Assert.Equal("tie", harness.Resolver.Items[first].SortName);
        Assert.Equal("Same", harness.Resolver.Items[first].Name);
        Assert.Equal("tie", harness.Resolver.Items[second].SortName);
        Assert.Equal("Same", harness.Resolver.Items[second].Name);

        var cycleStart = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.QueueSaturated, cycleStart.Outcome);
        Assert.Equal(new[] { before, first }, harness.AcceptedItems());

        // The host now returns the tied pair in the other relative order while
        // the anchor is the first of the pair. The identity anchor locates it,
        // and the run resumes strictly after it, so the tied sibling is not
        // covered in this cycle. This is the ADR-029 clause 5 registered residual
        // for rows tying on the exact pair: coverage is best-effort there, not
        // guaranteed, and this fact deliberately does not assert the tie as
        // covered.
        harness.DrainAndClearOutcomes();
        harness.SwapItems(first, second);

        var afterFlip = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        Assert.Equal(LibraryReconciliationOutcome.Completed, afterFlip.Outcome);
        Assert.Equal(new[] { after }, harness.AcceptedItems());
        Assert.Empty(harness.OutcomesFor(second));
    }

    private static async Task<(int RowsServed, int Covered, IReadOnlyList<(int Start, int Max)> Requests)> MeasureRunReachAsync(
        int scopeSize,
        int batchSize,
        int capacity)
    {
        using var harness = new CoverageHarness(batchSize, capacity);
        var items = harness.AddMovies(scopeSize);

        // The anchor sits near the end of the order, so the locate walk must
        // reach across the whole candidate list before the run covers the tail.
        harness.WriteCursor(
            items[scopeSize - 3],
            "m" + (scopeSize - 2).ToString("D2", CultureInfo.InvariantCulture));

        var result = await harness.Service.ReconcileAsync(
            LibraryReconciliationSource.Scheduled,
            progress: null,
            CancellationToken.None);

        return (harness.RowsServed, result.Covered, harness.Requests);
    }

    private static PluginConfiguration WithLibraries(params string[] libraryIds)
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = true,
        };

        foreach (var libraryId in libraryIds)
        {
            configuration.EnabledLibraries.Add(libraryId);
        }

        return configuration;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return condition();
    }

    /// <summary>
    /// A harness for the real whole-scope reconciliation path over an in-memory
    /// library, a real temporary state repository, and the real bounded work
    /// queue. The queue is wrapped only to record the bounded outcome it returned
    /// for each enqueue attempt.
    /// </summary>
    private sealed class CoverageHarness : IDisposable
    {
        private readonly string _root;

        public CoverageHarness(int batchSize, int queueCapacity)
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-f4-matrix-" + Guid.NewGuid().ToString("N"));
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
                Limits = new OperationalLimits
                {
                    ReconciliationBatchSize = batchSize,
                    QueueCapacity = queueCapacity,
                },
            });

            Repository = new StateRepository(_root, Configuration.Current.Limits);
            Fences = new ArtworkLifecycleFenceStore(Repository);
            Cursor = new ReconciliationCursorStore(Repository);
            Resolver = new ReconciliationLibraryResolver();

            var proxy = DispatchProxy.Create<ILibraryManager, HostOrderedLibraryManager>();
            Manager = (HostOrderedLibraryManager)(object)proxy;
            Enumerator = new JellyfinMediaLibraryEnumerator(proxy);

            Queue = new LibraryWorkQueue(() => Configuration.Current.Limits);
            Sink = new QueueRecordingSink(Queue);
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

        public HostOrderedLibraryManager Manager { get; }

        public JellyfinMediaLibraryEnumerator Enumerator { get; }

        public LibraryWorkQueue Queue { get; }

        public QueueRecordingSink Sink { get; }

        public LibraryReconciliationService Service { get; }

        public IReadOnlyList<(int Start, int Max)> Requests => Manager.Requests;

        public int RowsServed => Manager.RowsServed;

        public Guid AddMovie(string sortName, string? name = null)
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Movie(id, ReconciliationFixtures.LibraryId);
            item.Name = name ?? sortName;
            item.SortName = sortName;
            Resolver.LibraryIds[id] = ReconciliationFixtures.LibraryId;
            Resolver.Items[id] = item;
            Manager.Items.Add(item);
            return id;
        }

        public IReadOnlyList<Guid> AddMovies(int count)
        {
            var items = new List<Guid>(count);
            for (var index = 0; index < count; index++)
            {
                items.Add(AddMovie("m" + (index + 1).ToString("D2", CultureInfo.InvariantCulture)));
            }

            return items;
        }

        public Guid AddSeries(string sortName, string? name = null)
        {
            var id = Guid.NewGuid();
            var item = ReconciliationFixtures.Series(id, ReconciliationFixtures.LibraryId);
            item.Name = name ?? sortName;
            item.SortName = sortName;
            Resolver.LibraryIds[id] = ReconciliationFixtures.LibraryId;
            Resolver.Items[id] = item;
            Manager.Items.Add(item);
            return id;
        }

        public void RemoveItem(Guid itemId)
        {
            Manager.Items.RemoveAll(item => item.Id == itemId);
            Resolver.Items.Remove(itemId);
            Resolver.LibraryIds.Remove(itemId);
        }

        public void SwapItems(Guid first, Guid second)
        {
            var firstIndex = Manager.Items.FindIndex(item => item.Id == first);
            var secondIndex = Manager.Items.FindIndex(item => item.Id == second);
            (Manager.Items[firstIndex], Manager.Items[secondIndex]) =
                (Manager.Items[secondIndex], Manager.Items[firstIndex]);
        }

        public LibraryWorkHint Hint(Guid itemId)
        {
            return new LibraryWorkHint(
                itemId,
                LibraryWorkReason.Reconciliation,
                Configuration.Current.ConfigurationVersion);
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

        public string CursorPath()
        {
            return Repository.Paths.GetRecordPath(
                StateAuthority.Cache,
                ReconciliationCursorStore.RecordKind,
                ReconciliationCursorStore.RecordId);
        }

        public IReadOnlyList<Guid> AcceptedItems()
        {
            return Sink.Outcomes
                .Where(entry => entry.Outcome == WorkHintEnqueueOutcome.Accepted)
                .Select(entry => entry.ItemId)
                .ToArray();
        }

        public IReadOnlyList<WorkHintEnqueueOutcome> OutcomesFor(Guid itemId)
        {
            return Sink.Outcomes
                .Where(entry => entry.ItemId == itemId)
                .Select(entry => entry.Outcome)
                .ToArray();
        }

        public List<Guid> DrainAndClearOutcomes()
        {
            var drained = new List<Guid>();
            while (Queue.Count > 0)
            {
                var item = Queue.DequeueAsync(CancellationToken.None).GetAwaiter().GetResult();
                drained.Add(item.Key.ItemId);
                Queue.CompleteProcessing(item.Key);
            }

            Sink.Clear();
            return drained;
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

    /// <summary>
    /// A <see cref="IWorkHintSink"/> wrapper over the real
    /// <see cref="LibraryWorkQueue"/> that records the bounded outcome the queue
    /// returned for each hint. It classifies nothing itself, so a Coalesced,
    /// InFlight, Overflow, or Stopped outcome asserted by a test was produced by
    /// the real queue.
    /// </summary>
    private sealed class QueueRecordingSink : IWorkHintSink
    {
        private readonly object _gate = new object();
        private readonly LibraryWorkQueue _queue;
        private readonly List<(Guid ItemId, WorkHintEnqueueOutcome Outcome)> _outcomes = new();

        public QueueRecordingSink(LibraryWorkQueue queue)
        {
            _queue = queue;
        }

        public IReadOnlyList<(Guid ItemId, WorkHintEnqueueOutcome Outcome)> Outcomes
        {
            get
            {
                lock (_gate)
                {
                    return _outcomes.ToArray();
                }
            }
        }

        public int Capacity => _queue.Capacity;

        public int Count => _queue.Count;

        public bool TryEnqueue(in LibraryWorkHint hint)
        {
            return _queue.TryEnqueue(in hint);
        }

        public WorkHintEnqueueOutcome Enqueue(in LibraryWorkHint hint)
        {
            var outcome = _queue.Enqueue(in hint);
            lock (_gate)
            {
                _outcomes.Add((hint.ItemId, outcome));
            }

            return outcome;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _outcomes.Clear();
            }
        }
    }

    /// <summary>
    /// A bounded <see cref="ILibraryManager"/> double that serves the pinned
    /// host's effective candidate order (ADR-029 clause 1): <c>SortName</c>
    /// ascending, then the raw <c>Name</c> ascending, with the double's insertion
    /// order as the unspecified tie-break for rows equal on both. It applies the
    /// movie/episode candidate filter and the supported <c>StartIndex</c>/<c>Limit</c>
    /// paging surface, and records every page read so a test can assert the page
    /// reach and the row count.
    /// </summary>
    public class HostOrderedLibraryManager : DispatchProxy
    {
        public List<BaseItem> Items { get; } = new();

        public List<(int Start, int Max)> Requests { get; } = new();

        public int RowsServed { get; private set; }

        public Action<int>? OnRequest { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetItemList))
            {
                var query = (InternalItemsQuery)args![0]!;
                var start = query.StartIndex ?? 0;
                var limit = query.Limit ?? 100;
                OnRequest?.Invoke(start);
                Requests.Add((start, limit));

                var page = HostOrdered(query).Skip(start).Take(limit).ToList();
                RowsServed += page.Count;
                return page;
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetCount))
            {
                return HostOrdered((InternalItemsQuery)args![0]!).Count;
            }

            throw new NotSupportedException(targetMethod.Name);
        }

        private List<BaseItem> HostOrdered(InternalItemsQuery query)
        {
            return Items
                .Where(item => IsCandidate(item, query))
                .OrderBy(item => item.SortName, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsCandidate(BaseItem item, InternalItemsQuery query)
        {
            var kinds = query.IncludeItemTypes;
            if (kinds is null || kinds.Length == 0)
            {
                return true;
            }

            foreach (var kind in kinds)
            {
                if ((kind == BaseItemKind.Movie && item is Movie)
                    || (kind == BaseItemKind.Episode && item is Episode))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A minimal lifecycle coordinator for the library-event trigger fact: the
    /// per-item path never drains a fence or handles a removal in this test, so
    /// the double only has to satisfy the boundary.
    /// </summary>
    private sealed class NoOpLifecycleCoordinator : IArtworkLifecycleCoordinator
    {
        public void ResetStaleFence()
        {
        }

        public Task<ArtworkLifecycleResult> DrainAsync(
            ArtworkLifecycleFence fence,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkLifecycleResult.Create(
                fence,
                ArtworkLifecycleOutcome.NothingToDo,
                "No-op test coordinator."));
        }

        public Task<ArtworkLifecycleResult> DrainForHostShutdownAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkLifecycleResult.Create(
                ArtworkLifecycleFence.Normal,
                ArtworkLifecycleOutcome.NothingToDo,
                "No-op test coordinator."));
        }

        public Task<ArtworkRemovalResult> HandleItemRemovedAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.Confirmed,
                "No-op test coordinator.",
                tombstoned: true));
        }
    }
}
