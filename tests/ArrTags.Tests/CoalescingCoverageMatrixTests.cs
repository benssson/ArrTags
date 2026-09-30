using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.Updates;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 19.4 F6 coalescing coverage matrix (ADR-023). The headline facts drive
/// the real post-save chain: the real <see cref="Plugin.UpdateConfiguration"/>
/// save path, the real <see cref="ConfigurationReconciliationTrigger"/> and
/// <see cref="LibraryReconciliationService"/> over a recording wrapper that
/// delegates to the real <see cref="LibraryWorkQueue"/>, the real hosted
/// <see cref="LibraryWorkWorker"/>, and the real composed Phase 6 processors
/// (<see cref="ArtworkRecoveringWorkItemProcessor"/> over
/// <see cref="ArtworkPublishingWorkItemProcessor"/> over
/// <see cref="MetadataReconciliationProcessor"/>) with the durable stores and
/// the real publisher/renderer pipeline. The recording wrapper classifies
/// nothing itself: the enqueue outcomes, discard classifications, and rendered
/// output asserted below are produced by the production types. No live Jellyfin
/// or Arr instance is required.
/// </summary>
/// <remarks>
/// Matrix map (task requirement -> facts):
/// <list type="number">
/// <item>save-while-pending ->
///   <see cref="PendingOldVersionWorkAtSaveTimeIsCoalescedThenReRenderedAtTheNewVersion"/>;</item>
/// <item>save-while-in-flight ->
///   <see cref="InFlightOldVersionWorkAtSaveTimeIsCoalescedThenReRenderedAtTheNewVersion"/>;</item>
/// <item>after-slot-release ordering ->
///   <see cref="TheStaleBasisReenqueueIsIssuedOnlyAfterTheInFlightSlotIsReleased"/>;</item>
/// <item>one further re-enqueue / no unbounded loop ->
///   <see cref="ASecondVersionAdvanceCausesExactlyOneFurtherReenqueueAndThenCompletes"/>;</item>
/// <item>ineligible/absent/identity-less second pass ->
///   <see cref="AnItemThatChangedWhileOldVersionWorkWasOutstandingIsReenqueuedOnceThenDiscardedForItsRealReason"/>
///   (absent, ineligible, identity-less, and connection-changed through the real processor);</item>
/// <item>non-version discard ->
///   <see cref="NonVersionDiscardsDoNotReenqueue"/> (the classification gate isolated with a
///   stale carried version) plus the non-version second passes of requirement 5;</item>
/// <item>stopped/full queue ->
///   <see cref="StoppedQueueDropsTheStaleBasisReenqueue"/>,
///   <see cref="FullQueueDropsTheStaleBasisReenqueue"/>;</item>
/// <item>version-blind key / per-surface single-flight ->
///   <see cref="WorkItemKeyRemainsVersionBlindAndSingleFlightIsPerItemConnectionAndSurface"/>.</item>
/// </list>
/// </remarks>
public sealed class CoalescingCoverageMatrixTests
{
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PendingOldVersionWorkAtSaveTimeIsCoalescedThenReRenderedAtTheNewVersion()
    {
        using var harness = new SaveChainHarness();
        var firstFingerprint = await PublishInitialPosterAsync(harness);
        var itemId = harness.Phase6.ItemId;
        var subjectKey = SubjectKey(harness);

        var processor = harness.CreateProcessor();
        await harness.Trigger.StartAsync(CancellationToken.None);

        LibraryWorkWorker? worker = null;
        try
        {
            // The subject's old-version work is pending in the real bounded queue
            // when the save lands (the worker is started only after the save, so
            // the pending state is deterministic).
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                harness.Phase6.Queue.Enqueue(new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, 1)));
            Assert.Equal(1, harness.Phase6.Queue.Count);

            var savedVersion = harness.Save(QualityTemplateConfiguration("Q{value}"));
            Assert.Equal(2, savedVersion);

            // The real save path activated the new snapshot and the real
            // post-save trigger enqueued the new-version hint; the real queue
            // coalesced it away because the version-blind key is pending. Without
            // the stale-basis re-enqueue the item keeps its v1 artwork.
            var postSave = await WaitForPostSaveHintAsync(harness, itemId, savedVersion);
            Assert.Equal(WorkHintEnqueueOutcome.Coalesced, postSave.Outcome);

            // Driving the worker now drains the pending v1 item: the stale pass
            // re-enqueues the current version, which re-renders.
            worker = harness.Phase6.CreateWorker(
                processor,
                workerCount: 1,
                shutdownTimeout: TimeSpan.FromSeconds(2));
            await worker.StartAsync(CancellationToken.None);

            await Phase6Wait.UntilAsync(
                () => harness.Phase6.Renderer.Calls == 2
                    && harness.Phase6.Queue.Count == 0
                    && harness.Phase6.Queue.InFlightCount == 0);

            await AssertStableAsync(() => processor.Calls, 2);

            var passes = SubjectPasses(processor, itemId);
            Assert.Equal(new long[] { 1, 2 }, passes.Select(item => item.ConfigurationVersion).ToArray());
            Assert.Equal(1, processor.PeakConcurrency(subjectKey));
            AssertReRenderedAtTheNewVersion(harness, firstFingerprint, "Q{value}");
        }
        finally
        {
            if (worker is not null)
            {
                await worker.StopAsync(CancellationToken.None);
            }

            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task InFlightOldVersionWorkAtSaveTimeIsCoalescedThenReRenderedAtTheNewVersion()
    {
        using var harness = new SaveChainHarness();
        var firstFingerprint = await PublishInitialPosterAsync(harness);
        var itemId = harness.Phase6.ItemId;
        var subjectKey = SubjectKey(harness);

        // The subject's old-version pass is held inside the processing path, so
        // the key is in flight for the whole save.
        var passEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = harness.CreateProcessor(item => item.Key == subjectKey && item.ConfigurationVersion == 1
            ? BlockAsync(passEntered, releasePass)
            : Task.CompletedTask);

        using var worker = harness.Phase6.CreateWorker(
            processor,
            workerCount: 1,
            shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        await harness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                harness.Phase6.Queue.Enqueue(new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, 1)));
            await passEntered.Task.WaitAsync(BoundedWait);
            Assert.Equal(1, harness.Phase6.Queue.InFlightCount);

            var savedVersion = harness.Save(QualityTemplateConfiguration("Q{value}"));
            Assert.Equal(2, savedVersion);

            // The new-version hint reaches the real queue while the version-blind
            // key is in flight, so it is coalesced away.
            var postSave = await WaitForPostSaveHintAsync(harness, itemId, savedVersion);
            Assert.Equal(WorkHintEnqueueOutcome.InFlight, postSave.Outcome);

            releasePass.TrySetResult();

            await Phase6Wait.UntilAsync(
                () => harness.Phase6.Renderer.Calls == 2
                    && harness.Phase6.Queue.Count == 0
                    && harness.Phase6.Queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 2);

            var passes = SubjectPasses(processor, itemId);
            Assert.Equal(new long[] { 1, 2 }, passes.Select(item => item.ConfigurationVersion).ToArray());
            Assert.Equal(1, processor.PeakConcurrency(subjectKey));
            AssertReRenderedAtTheNewVersion(harness, firstFingerprint, "Q{value}");
        }
        finally
        {
            releasePass.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TheStaleBasisReenqueueIsIssuedOnlyAfterTheInFlightSlotIsReleased()
    {
        // ADR-023 clause 3: a same-key enqueue issued from inside the processing
        // path is coalesced away by the real queue, so the worker's stale-basis
        // re-enqueue is effective only because the worker releases the in-flight
        // slot first. This fact proves the ordering directly: the pre-release
        // attempt from inside the processing path is rejected as InFlight, and
        // the worker's own post-release enqueue is accepted and processed. If
        // the worker issued its re-enqueue before CompleteProcessing, the fresh
        // item would be silently dropped and this fact would fail (one pass, one
        // render).
        using var harness = new SaveChainHarness();
        var firstFingerprint = await PublishInitialPosterAsync(harness);
        var itemId = harness.Phase6.ItemId;
        var subjectKey = SubjectKey(harness);

        Assert.True(harness.Phase6.Configuration.TryReplace(QualityTemplateConfiguration("Q{value}"), out _));
        var savedVersion = harness.Phase6.Configuration.Current.ConfigurationVersion;
        Assert.Equal(2, savedVersion);

        WorkHintEnqueueOutcome? preReleaseOutcome = null;
        var processor = harness.CreateProcessor(item =>
        {
            if (item.Key == subjectKey && item.ConfigurationVersion == 1)
            {
                preReleaseOutcome = harness.Phase6.Queue.Enqueue(
                    new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, savedVersion));
            }

            return Task.CompletedTask;
        });

        using var worker = harness.Phase6.CreateWorker(
            processor,
            workerCount: 1,
            shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                harness.Phase6.Queue.Enqueue(new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, 1)));

            await Phase6Wait.UntilAsync(
                () => processor.Results.Length == 2
                    && harness.Phase6.Queue.Count == 0
                    && harness.Phase6.Queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 2);

            Assert.Equal(WorkHintEnqueueOutcome.InFlight, preReleaseOutcome);
            var passes = SubjectPasses(processor, itemId);
            Assert.Equal(new long[] { 1, 2 }, passes.Select(item => item.ConfigurationVersion).ToArray());
            Assert.Equal(1, processor.PeakConcurrency(subjectKey));
            AssertReRenderedAtTheNewVersion(harness, firstFingerprint, "Q{value}");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ASecondVersionAdvanceCausesExactlyOneFurtherReenqueueAndThenCompletes()
    {
        // ADR-023 clause 4: the bound is the version the fresh item carries, so a
        // further version advance causes exactly one further re-enqueue (v1 ->
        // v2 -> v3 here) and the final current-version pass completes without any
        // further pass. Both new-version post-save hints are coalesced (the key
        // is pending/in flight), so both re-enqueues come from the worker.
        using var harness = new SaveChainHarness();
        var firstFingerprint = await PublishInitialPosterAsync(harness);
        var itemId = harness.Phase6.ItemId;
        var subjectKey = SubjectKey(harness);

        var pass1Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePass1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pass2Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePass2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = harness.CreateProcessor(item => item.Key != subjectKey
            ? Task.CompletedTask
            : item.ConfigurationVersion switch
            {
                1 => BlockAsync(pass1Entered, releasePass1),
                2 => BlockAsync(pass2Entered, releasePass2),
                _ => Task.CompletedTask,
            });

        using var worker = harness.Phase6.CreateWorker(
            processor,
            workerCount: 1,
            shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        await harness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                harness.Phase6.Queue.Enqueue(new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, 1)));
            await pass1Entered.Task.WaitAsync(BoundedWait);

            var version2 = harness.Save(QualityTemplateConfiguration("Q{value}"));
            Assert.Equal(2, version2);
            var firstHint = await WaitForPostSaveHintAsync(harness, itemId, version2);
            Assert.Equal(WorkHintEnqueueOutcome.InFlight, firstHint.Outcome);

            // The v1 pass discards stale and the worker re-enqueues the fresh v2
            // item; hold that pass so the second save lands while it is in flight.
            releasePass1.TrySetResult();
            await pass2Entered.Task.WaitAsync(BoundedWait);

            var version3 = harness.Save(QualityTemplateConfiguration("Q2{value}"));
            Assert.Equal(3, version3);
            var secondHint = await WaitForPostSaveHintAsync(harness, itemId, version3);
            Assert.Equal(WorkHintEnqueueOutcome.InFlight, secondHint.Outcome);

            releasePass2.TrySetResult();

            await Phase6Wait.UntilAsync(
                () => harness.Phase6.Renderer.Calls == 2
                    && harness.Phase6.Queue.Count == 0
                    && harness.Phase6.Queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 3);

            var passes = SubjectPasses(processor, itemId);
            Assert.Equal(new long[] { 1, 2, 3 }, passes.Select(item => item.ConfigurationVersion).ToArray());
            Assert.Equal(
                new DiscardReason?[]
                {
                    DiscardReason.ConfigurationStale,
                    DiscardReason.ConfigurationStale,
                    null,
                },
                processor.Results.Select(result => result.DiscardReason).ToArray());
            Assert.Equal(1, processor.PeakConcurrency(subjectKey));
            AssertReRenderedAtTheNewVersion(harness, firstFingerprint, "Q2{value}");
        }
        finally
        {
            releasePass1.TrySetResult();
            releasePass2.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("ineligible")]
    [InlineData("identity-less")]
    [InlineData("connection-changed")]
    public async Task AnItemThatChangedWhileOldVersionWorkWasOutstandingIsReenqueuedOnceThenDiscardedForItsRealReason(
        string mutation)
    {
        // ADR-023 clause 5: the version check precedes the item resolve and
        // eligibility checks, so an item that became absent, ineligible,
        // identity-less, or connection-changed while old-version work was
        // outstanding is re-enqueued once as a ConfigurationStale discard and the
        // fresh pass is then discarded for its real reason without a further
        // re-enqueue. The real processor produces every classification here; the
        // only scripted part is the mutation the operator/host causes.
        using var harness = new SaveChainHarness();
        var firstFingerprint = await PublishInitialPosterAsync(harness);
        var itemId = harness.Phase6.ItemId;
        var subjectKey = SubjectKey(harness);

        var pass1Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePass1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = harness.CreateProcessor(item => item.Key == subjectKey && item.ConfigurationVersion == 1
            ? BlockAsync(pass1Entered, releasePass1)
            : Task.CompletedTask);

        using var worker = harness.Phase6.CreateWorker(
            processor,
            workerCount: 1,
            shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        await harness.Trigger.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                harness.Phase6.Queue.Enqueue(new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, 1)));
            await pass1Entered.Task.WaitAsync(BoundedWait);

            // The connection-changed case changes the item's basis through the
            // save itself (the provider connection is gone in the new snapshot).
            var savedVersion = mutation == "connection-changed"
                ? harness.Save(RadarrDisabledConfiguration())
                : harness.Save(QualityTemplateConfiguration("Q{value}"));
            Assert.Equal(2, savedVersion);

            if (mutation != "connection-changed")
            {
                // The post-save new-version hint is coalesced while the stale
                // work is in flight, so the fresh pass can only come from the
                // worker's stale-basis re-enqueue.
                var postSave = await WaitForPostSaveHintAsync(harness, itemId, savedVersion);
                Assert.Equal(WorkHintEnqueueOutcome.InFlight, postSave.Outcome);
            }

            // The item changes while the old-version work is still outstanding.
            ApplyItemMutation(harness, mutation);
            releasePass1.TrySetResult();

            var expected = mutation switch
            {
                "absent" => DiscardReason.ItemMissing,
                "ineligible" => DiscardReason.Ineligible,
                "identity-less" => DiscardReason.IdentityUnavailable,
                _ => DiscardReason.ConnectionChanged,
            };

            await Phase6Wait.UntilAsync(
                () => processor.Results.Length == 2
                    && harness.Phase6.Queue.Count == 0
                    && harness.Phase6.Queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 2);

            var passes = SubjectPasses(processor, itemId);
            Assert.Equal(new long[] { 1, 2 }, passes.Select(item => item.ConfigurationVersion).ToArray());
            Assert.Equal(
                new DiscardReason?[] { DiscardReason.ConfigurationStale, expected },
                processor.Results.Select(result => result.DiscardReason).ToArray());
            Assert.Equal(1, processor.PeakConcurrency(subjectKey));

            // The discards never reached artwork publication: the published
            // poster is the v1 output, unchanged.
            Assert.Equal(1, harness.Phase6.Renderer.Calls);
            Assert.Equal(1, harness.Phase6.Host.SaveCalls);
            Assert.Equal(
                firstFingerprint,
                harness.Phase6.States.Read(itemId, Phase6Harness.Surface).Value!.PublishedFingerprint);
        }
        finally
        {
            releasePass1.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
            await harness.Trigger.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(DiscardReason.ItemMissing)]
    [InlineData(DiscardReason.Ineligible)]
    [InlineData(DiscardReason.ConnectionChanged)]
    [InlineData(DiscardReason.IdentityUnavailable)]
    public async Task NonVersionDiscardsDoNotReenqueue(DiscardReason discardReason)
    {
        // ADR-023 clause 2: only ConfigurationStale re-enqueues. The real
        // processor cannot produce a non-version discard for stale-versioned
        // work by construction (the version check precedes everything, so a real
        // non-version discard always carries the current version and the
        // worker's same-version guard also applies). The classification gate is
        // therefore isolated with a delegating processor double that returns the
        // bounded classification for an item whose carried version differs from
        // the current one: exactly the shape the gate must reject. A broken gate
        // re-enqueues the current version and this fact observes a second pass.
        using var queue = new LibraryWorkQueue(8);
        var configuration = new ConfigurationSnapshotService(new PluginConfiguration());
        Assert.True(configuration.TryReplace(new PluginConfiguration(), out _));
        Assert.Equal(2, configuration.Current.ConfigurationVersion);

        var processor = new ChainRecordingProcessor(new ScriptedProcessor(
            (_, _) => Task.FromResult(WorkProcessingResult.Discarded(discardReason, "discarded"))));
        using var worker = new LibraryWorkWorker(
            queue,
            processor,
            configuration,
            workerCount: 1,
            boundedShutdownTimeout: TimeSpan.FromSeconds(2));

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            queue.Enqueue(new LibraryWorkItem(
                new WorkItemKey(Guid.NewGuid(), null, ArtworkImageSurface.Primary),
                LibraryWorkReason.Updated,
                1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Phase6Wait.UntilAsync(() => processor.Results.Length == 1 && queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 1);

            Assert.Equal(0, queue.Count);
            Assert.Equal(discardReason, processor.Results[0].DiscardReason);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StoppedQueueDropsTheStaleBasisReenqueue()
    {
        // ADR-023 clause 2: a stopped queue drops the re-enqueue (bounded, never
        // blocking). The stale pass is processed, the re-enqueue hits the stopped
        // queue, and the item is not re-rendered.
        using var harness = new SaveChainHarness();
        var firstFingerprint = await PublishInitialPosterAsync(harness);
        var itemId = harness.Phase6.ItemId;
        var subjectKey = SubjectKey(harness);

        Assert.True(harness.Phase6.Configuration.TryReplace(QualityTemplateConfiguration("Q{value}"), out _));
        Assert.Equal(2, harness.Phase6.Configuration.Current.ConfigurationVersion);

        var stopped = false;
        var processor = harness.CreateProcessor(item =>
        {
            if (item.Key == subjectKey && item.ConfigurationVersion == 1)
            {
                // The queue stops accepting while the stale item is in flight.
                harness.Phase6.Queue.StopAccepting();
                stopped = true;
            }

            return Task.CompletedTask;
        });

        using var worker = harness.Phase6.CreateWorker(
            processor,
            workerCount: 1,
            shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                harness.Phase6.Queue.Enqueue(new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, 1)));

            await Phase6Wait.UntilAsync(
                () => processor.Results.Length == 1
                    && harness.Phase6.Queue.Count == 0
                    && harness.Phase6.Queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 1);

            Assert.True(stopped);
            Assert.False(harness.Phase6.Queue.IsAccepting);
            Assert.Equal(DiscardReason.ConfigurationStale, processor.Results[0].DiscardReason);
            Assert.Equal(1, harness.Phase6.Renderer.Calls);
            Assert.Equal(
                firstFingerprint,
                harness.Phase6.States.Read(itemId, Phase6Harness.Surface).Value!.PublishedFingerprint);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task FullQueueDropsTheStaleBasisReenqueue()
    {
        // ADR-023 clause 2: a full queue drops the re-enqueue. The bounded
        // pending capacity is filled by an unrelated key while the stale item is
        // in flight, so the re-enqueue hits the same bound as any other trigger.
        using var harness = new SaveChainHarness(queueCapacity: 1);
        var firstFingerprint = await PublishInitialPosterAsync(harness);
        var itemId = harness.Phase6.ItemId;
        var subjectKey = SubjectKey(harness);

        var replacement = QualityTemplateConfiguration("Q{value}");
        replacement.Limits = new OperationalLimits { QueueCapacity = 1 };
        Assert.True(harness.Phase6.Configuration.TryReplace(replacement, out _));

        var fillerKey = new WorkItemKey(Guid.NewGuid(), null, ArtworkImageSurface.Primary);
        var processor = harness.CreateProcessor(item =>
        {
            if (item.Key == subjectKey && item.ConfigurationVersion == 1)
            {
                Assert.Equal(
                    WorkHintEnqueueOutcome.Accepted,
                    harness.Phase6.Queue.Enqueue(new LibraryWorkItem(fillerKey, LibraryWorkReason.Updated, 2)));
            }

            return Task.CompletedTask;
        });

        using var worker = harness.Phase6.CreateWorker(
            processor,
            workerCount: 1,
            shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                harness.Phase6.Queue.Enqueue(new LibraryWorkItem(subjectKey, LibraryWorkReason.Updated, 1)));

            await Phase6Wait.UntilAsync(
                () => processor.Results.Length == 2
                    && harness.Phase6.Queue.Count == 0
                    && harness.Phase6.Queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 2);

            // The subject was processed exactly once (the stale pass); the
            // filler was processed once and no second subject item appeared.
            Assert.Single(processor.Items, item => item.Key == subjectKey);
            Assert.Single(processor.Items, item => item.Key == fillerKey);
            Assert.Equal(1, harness.Phase6.Renderer.Calls);
            Assert.Equal(
                firstFingerprint,
                harness.Phase6.States.Read(itemId, Phase6Harness.Surface).Value!.PublishedFingerprint);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WorkItemKeyRemainsVersionBlindAndSingleFlightIsPerItemConnectionAndSurface()
    {
        var itemId = Guid.NewGuid();
        const string connection = "radarr:http://radarr.test";
        var primary = new WorkItemKey(itemId, connection, ArtworkImageSurface.Primary);
        var indexed = new WorkItemKey(itemId, connection, new ArtworkImageSurface(ArtworkImageType.Primary, 1));

        // The key carries no configuration version: hints that differ only in
        // the carried version produce the same key and the same hash, and the
        // key type has no version member at all.
        Assert.Equal(
            WorkItemKey.FromHint(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 1)),
            WorkItemKey.FromHint(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 9)));
        Assert.Equal(
            WorkItemKey.FromHint(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 1)).GetHashCode(),
            WorkItemKey.FromHint(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 9)).GetHashCode());
        var keyMembers = typeof(WorkItemKey)
            .GetProperties()
            .Select(property => property.Name)
            .Concat(typeof(WorkItemKey)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.Name));
        Assert.DoesNotContain(keyMembers, name => name.Contains("Version", StringComparison.OrdinalIgnoreCase));

        using var queue = new LibraryWorkQueue(8);

        // The real queue is version-blind: a pending or in-flight key with an
        // old-version item coalesces a hint whose carried version differs, so the
        // version never affects the coalescing identity.
        Assert.Equal(WorkHintEnqueueOutcome.Accepted, queue.Enqueue(new LibraryWorkItem(primary, LibraryWorkReason.Updated, 1)));
        Assert.Equal(WorkHintEnqueueOutcome.Coalesced, queue.Enqueue(new LibraryWorkItem(primary, LibraryWorkReason.Updated, 9)));

        var inFlight = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(primary, inFlight.Key);
        Assert.Equal(1, queue.InFlightCount);
        Assert.Equal(WorkHintEnqueueOutcome.InFlight, queue.Enqueue(new LibraryWorkItem(primary, LibraryWorkReason.Updated, 10)));

        // Single-flight is per (item, connection, surface): a distinct surface
        // is a distinct key and is accepted while the primary key is in flight,
        // and a second hint for that surface coalesces while it is pending.
        Assert.Equal(WorkHintEnqueueOutcome.Accepted, queue.Enqueue(new LibraryWorkItem(indexed, LibraryWorkReason.Updated, 10)));
        Assert.Equal(WorkHintEnqueueOutcome.Coalesced, queue.Enqueue(new LibraryWorkItem(indexed, LibraryWorkReason.Updated, 11)));

        Assert.Equal(primary, new WorkItemKey(itemId, connection, ArtworkImageSurface.Primary));
        Assert.NotEqual(primary, indexed);
        Assert.NotEqual(primary, new WorkItemKey(itemId, "sonarr:http://sonarr.test", ArtworkImageSurface.Primary));

        // Releasing the in-flight slot admits the primary key again (the bound
        // is the version the fresh item carries, not a retained version set).
        queue.CompleteProcessing(inFlight.Key);
        Assert.Equal(0, queue.InFlightCount);
        Assert.Equal(WorkHintEnqueueOutcome.Accepted, queue.Enqueue(new LibraryWorkItem(primary, LibraryWorkReason.Updated, 11)));
        Assert.Equal(2, queue.Count);
    }

    private static WorkItemKey SubjectKey(SaveChainHarness harness)
    {
        return new WorkItemKey(harness.Phase6.ItemId, null, ArtworkImageSurface.Primary);
    }

    private static LibraryWorkItem[] SubjectPasses(ChainRecordingProcessor processor, Guid itemId)
    {
        return processor.Items.Where(item => item.Key.ItemId == itemId).ToArray();
    }

    private static async Task BlockAsync(TaskCompletionSource entered, TaskCompletionSource release)
    {
        entered.TrySetResult();
        await release.Task;
    }

    private static async Task<string> PublishInitialPosterAsync(SaveChainHarness harness)
    {
        await harness.Phase6.Recovering.ProcessAsync(harness.Phase6.WorkItem(), CancellationToken.None);
        Assert.Equal(1, harness.Phase6.Renderer.Calls);
        Assert.Equal(1, harness.Phase6.Host.SaveCalls);

        var fingerprint = harness.Phase6.States
            .Read(harness.Phase6.ItemId, Phase6Harness.Surface)
            .Value?.PublishedFingerprint;
        Assert.False(string.IsNullOrEmpty(fingerprint));
        return fingerprint!;
    }

    private static void AssertReRenderedAtTheNewVersion(
        SaveChainHarness harness,
        string firstFingerprint,
        string expectedTemplate)
    {
        Assert.Equal(2, harness.Phase6.Host.SaveCalls);
        Assert.Equal(2, harness.Phase6.Renderer.Calls);
        Assert.Contains(
            harness.Phase6.Renderer.LastRequest!.BadgeDefinitions,
            definition => definition.Selector == BadgeSelector.Quality && definition.Template == expectedTemplate);

        var secondFingerprint = harness.Phase6.States
            .Read(harness.Phase6.ItemId, Phase6Harness.Surface)
            .Value!.PublishedFingerprint;
        Assert.NotNull(secondFingerprint);
        Assert.NotEqual(firstFingerprint, secondFingerprint);
    }

    private static async Task<PostSaveHint> WaitForPostSaveHintAsync(SaveChainHarness harness, Guid itemId, long version)
    {
        await Phase6Wait.UntilAsync(
            () => harness.Sink.Outcomes.Any(outcome => outcome.ItemId == itemId && outcome.ConfigurationVersion == version));

        return Assert.Single(
            harness.Sink.Outcomes,
            outcome => outcome.ItemId == itemId && outcome.ConfigurationVersion == version);
    }

    private static void ApplyItemMutation(SaveChainHarness harness, string mutation)
    {
        var itemId = harness.Phase6.ItemId;
        switch (mutation)
        {
            case "absent":
                harness.Phase6.Library.Items.Remove(itemId);
                harness.Phase6.Library.LibraryIds.Remove(itemId);
                break;
            case "ineligible":
                Assert.IsType<ReconciliationTestMovie>(harness.Phase6.Library.Items[itemId]).Location = LocationType.Remote;
                break;
            case "identity-less":
                harness.Phase6.Library.Items[itemId] = ReconciliationFixtures.Movie(Guid.Empty, ReconciliationFixtures.LibraryId);
                break;
            case "connection-changed":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown F6 mutation.");
        }
    }

    private static PluginConfiguration QualityTemplateConfiguration(string template)
    {
        var configuration = RadarrEnabledConfiguration();
        configuration.Renderer = new RendererConfiguration
        {
            Selectors = new Collection<BadgeSelectorConfiguration>
            {
                new BadgeSelectorConfiguration
                {
                    Selector = BadgeSelector.Quality,
                    Enabled = true,
                    Template = template,
                },
            },
        };

        return configuration;
    }

    private static PluginConfiguration RadarrEnabledConfiguration()
    {
        return new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
        };
    }

    private static PluginConfiguration RadarrDisabledConfiguration()
    {
        var configuration = RadarrEnabledConfiguration();
        configuration.Radarr = new ArrConnectionConfiguration { Enabled = false };
        return configuration;
    }

    private static async Task AssertStableAsync(Func<int> observe, int expected, int stabilityMilliseconds = 250)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(stabilityMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Assert.Equal(expected, observe());
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// The F6 coalescing matrix harness. It composes the production save path
    /// (<see cref="Plugin.UpdateConfiguration"/>), the real post-save
    /// <see cref="ConfigurationReconciliationTrigger"/> over the real
    /// <see cref="LibraryReconciliationService"/> and the real
    /// <see cref="LibraryWorkQueue"/> (through the recording sink), and the task
    /// 6.8 <see cref="Phase6Harness"/> durable processor/publisher/renderer graph.
    /// The worker is created per fact so each fact controls its bounded pool and
    /// recording wrapper.
    /// </summary>
    private sealed class SaveChainHarness : IDisposable
    {
        private readonly string _root;
        private readonly ConfigurationActivationTests.TestXmlSerializer _serializer;
        private readonly ServiceProvider _provider;

        public SaveChainHarness(int queueCapacity = 512)
        {
            _root = Path.Combine(Path.GetTempPath(), "arrtags-19-4-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "plugins"));
            Directory.CreateDirectory(Path.Combine(_root, "configurations"));

            Phase6 = new Phase6Harness(configuration: new PluginConfiguration
            {
                BadgeMoviePosters = true,
                Radarr = new ArrConnectionConfiguration
                {
                    Enabled = true,
                    BaseUrl = "http://radarr.test",
                    ApiKey = "test-radarr-api-key",
                },
                Limits = new OperationalLimits { QueueCapacity = queueCapacity },
            });
            Phase6.Reader.Handler = Phase6Harness.Matched();
            Phase6.Host.CurrentBytes = Phase6Harness.Original();

            Enumerator = new FakeMediaLibraryEnumerator();
            Enumerator.Items.Add(Phase6.Library.Items[Phase6.ItemId]);
            Sink = new OutcomeRecordingSink(Phase6.Queue);
            Service = new LibraryReconciliationService(
                Phase6.Configuration,
                Phase6.Library,
                Enumerator,
                Sink,
                Phase6.Fences,
                new ReconciliationCursorStore(Phase6.Repository));
            Trigger = new ConfigurationReconciliationTrigger(Service, TimeSpan.FromSeconds(2));

            var paths = DispatchProxy.Create<IApplicationPaths, ConfigurationActivationTests.TestApplicationPaths>();
            ((ConfigurationActivationTests.TestApplicationPaths)(object)paths).RootPath = _root;
            Paths = paths;

            _serializer = new ConfigurationActivationTests.TestXmlSerializer();
            _serializer.SerializeToFile(
                new PluginConfiguration { WebhookSecret = "stable-webhook-secret-7a1c" },
                Path.Combine(_root, "configurations", "ArrTags.xml"));

            var services = new ServiceCollection();
            services.AddSingleton(Phase6.Configuration);
            services.AddSingleton<IConfigurationReconciliationTrigger>(Trigger);
            _provider = services.BuildServiceProvider();

            Plugin = new Plugin(Paths, _serializer, _provider);
        }

        public Phase6Harness Phase6 { get; }

        public FakeMediaLibraryEnumerator Enumerator { get; }

        public OutcomeRecordingSink Sink { get; }

        public LibraryReconciliationService Service { get; }

        public ConfigurationReconciliationTrigger Trigger { get; }

        public IApplicationPaths Paths { get; }

        public Plugin Plugin { get; }

        public ChainRecordingProcessor CreateProcessor(Func<LibraryWorkItem, Task>? before = null)
        {
            return new ChainRecordingProcessor(Phase6.Recovering, before);
        }

        public long Save(PluginConfiguration candidate)
        {
            var previous = Phase6.Configuration.Current.ConfigurationVersion;
            Plugin.UpdateConfiguration(candidate);

            Assert.True(Plugin.LastConfigurationValidationResult is { IsValid: true });
            var current = Phase6.Configuration.Current.ConfigurationVersion;
            Assert.Equal(previous + 1, current);
            return current;
        }

        public void Dispose()
        {
            Trigger.Dispose();
            _provider.Dispose();
            _serializer.Dispose();
            Phase6.Dispose();

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
    /// A recording wrapper around one production <see cref="IWorkItemProcessor"/>.
    /// It delegates every pass to the inner processor, records the bounded items
    /// and the bounded results the production pipeline returned, and tracks the
    /// peak per-key concurrency so a fact can assert single-flight. It classifies
    /// nothing itself, and the optional before hook only schedules test world
    /// changes (block, stop, fill) before the delegated pass.
    /// </summary>
    private sealed class ChainRecordingProcessor : IWorkItemProcessor
    {
        private const int MaxRecordedEntries = 64;

        private readonly object _gate = new();
        private readonly IWorkItemProcessor _inner;
        private readonly Func<LibraryWorkItem, Task>? _before;
        private readonly List<LibraryWorkItem> _items = new();
        private readonly List<WorkProcessingResult> _results = new();
        private readonly Dictionary<WorkItemKey, int> _inFlight = new();
        private readonly Dictionary<WorkItemKey, int> _peak = new();
        private int _calls;

        public ChainRecordingProcessor(IWorkItemProcessor inner, Func<LibraryWorkItem, Task>? before = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _before = before;
        }

        public int Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls;
                }
            }
        }

        public LibraryWorkItem[] Items
        {
            get
            {
                lock (_gate)
                {
                    return _items.ToArray();
                }
            }
        }

        public WorkProcessingResult[] Results
        {
            get
            {
                lock (_gate)
                {
                    return _results.ToArray();
                }
            }
        }

        public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _calls++;
                if (_items.Count < MaxRecordedEntries)
                {
                    _items.Add(item);
                }

                _inFlight.TryGetValue(item.Key, out var current);
                current++;
                _inFlight[item.Key] = current;
                _peak.TryGetValue(item.Key, out var peak);
                if (current > peak)
                {
                    _peak[item.Key] = current;
                }
            }

            try
            {
                if (_before is not null)
                {
                    await _before(item);
                }

                var result = await _inner.ProcessAsync(item, cancellationToken);
                lock (_gate)
                {
                    if (_results.Count < MaxRecordedEntries)
                    {
                        _results.Add(result);
                    }
                }

                return result;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight[item.Key] = _inFlight[item.Key] - 1;
                }
            }
        }

        public int PeakConcurrency(WorkItemKey key)
        {
            lock (_gate)
            {
                return _peak.TryGetValue(key, out var peak) ? peak : 0;
            }
        }
    }

    /// <summary>
    /// A minimal scripted <see cref="IWorkItemProcessor"/> for the classification
    /// gate isolation. It returns the supplied bounded classification for every
    /// item without any production processing.
    /// </summary>
    private sealed class ScriptedProcessor : IWorkItemProcessor
    {
        private readonly Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>> _handler;

        public ScriptedProcessor(Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
        {
            return _handler(item, cancellationToken);
        }
    }

    /// <summary>
    /// An <see cref="IWorkHintSink"/> wrapper over the real
    /// <see cref="LibraryWorkQueue"/> that records the bounded outcome the queue
    /// returned for each hint. It classifies nothing itself, so an outcome
    /// asserted by a fact was produced by the real queue.
    /// </summary>
    private sealed class OutcomeRecordingSink : IWorkHintSink
    {
        private readonly object _gate = new();
        private readonly LibraryWorkQueue _queue;
        private readonly List<PostSaveHint> _outcomes = new();

        public OutcomeRecordingSink(LibraryWorkQueue queue)
        {
            _queue = queue;
        }

        public IReadOnlyList<PostSaveHint> Outcomes
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
                _outcomes.Add(new PostSaveHint(hint.ItemId, hint.ConfigurationVersion, outcome));
            }

            return outcome;
        }
    }

    private readonly record struct PostSaveHint(Guid ItemId, long ConfigurationVersion, WorkHintEnqueueOutcome Outcome);
}
