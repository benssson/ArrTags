using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Rendering;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 19.3 coverage for the ADR-023 F6 mechanism: a work item that discarded
/// with the <see cref="DiscardReason.ConfigurationStale"/> classification
/// re-enqueues exactly one fresh work item at the current configuration version
/// after the in-flight slot is released, bounded per (item, connection, image
/// surface, version). A discard with any other classification does not
/// re-enqueue, a stopped or full queue drops the bounded re-enqueue, and the
/// version-blind key and single-flight are unchanged. No live Jellyfin or Arr
/// instance is required.
/// </summary>
public sealed class StaleBasisReenqueueTests
{
    private static readonly Guid ItemId = Guid.Parse("19d3aaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly WorkItemKey Key = new(ItemId, null, ArtworkImageSurface.Primary);

    [Fact]
    public async Task StaleBasisDiscardReenqueuesOneFreshItemAtTheCurrentVersion()
    {
        using var queue = new LibraryWorkQueue(8);
        var configuration = new ConfigurationSnapshotService(ValidConfiguration());

        // The save activates version 2 while the outstanding item still carries
        // the pre-save version 1 (the post-save hint was coalesced away).
        Assert.True(configuration.TryReplace(ValidConfiguration(), out _));
        Assert.Equal(2, configuration.Current.ConfigurationVersion);

        var processor = new StaleBasisRecordingProcessor((item, _) =>
            Task.FromResult(item.ConfigurationVersion == 2
                ? WorkProcessingResult.Completed()
                : WorkProcessingResult.Discarded(DiscardReason.ConfigurationStale, "The work basis is stale.")));
        using var worker = CreateWorker(queue, processor, configuration);

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            queue.Enqueue(new LibraryWorkItem(Key, LibraryWorkReason.Updated, 1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Exactly one re-enqueue: the first item discards, the fresh item
            // carries the current version and completes without re-enqueueing.
            await Phase6Wait.UntilAsync(() => processor.Calls == 2);
            await Phase6Wait.UntilAsync(() => queue.Count == 0 && queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 2);

            var processed = processor.Items;
            Assert.Equal(new long[] { 1, 2 }, processed.Select(item => item.ConfigurationVersion).ToArray());
            Assert.All(processed, item => Assert.Equal(Key, item.Key));
            Assert.All(processed, item => Assert.Equal(LibraryWorkReason.Updated, item.Reason));
            Assert.Equal(1, processor.PeakConcurrency(Key));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AReenqueueIssuedBeforeTheSlotIsReleasedWouldBeCoalescedAway()
    {
        // ADR-023 context, verified against the shipped queue: a same-key enqueue
        // issued from inside the processing path (before the worker's finally
        // releases the in-flight slot) is coalesced away. That is exactly why the
        // worker releases the slot first; the companion worker test above fails if
        // the re-enqueue is issued before the release, because the fresh item is
        // then silently dropped instead of being processed.
        using var queue = new LibraryWorkQueue(8);
        var configuration = new ConfigurationSnapshotService(ValidConfiguration());

        WorkHintEnqueueOutcome? preReleaseOutcome = null;
        var processor = new StaleBasisRecordingProcessor((item, _) =>
        {
            // The same key is still in flight here, so the queue coalesces the
            // re-enqueue regardless of the version it carries (the key is
            // version-blind). The item carries the current version so the worker's
            // own stale-basis re-enqueue does not add a second pass in this test.
            preReleaseOutcome = queue.Enqueue(
                new LibraryWorkItem(item.Key, item.Reason, configuration.Current.ConfigurationVersion));
            return Task.FromResult(WorkProcessingResult.Discarded(DiscardReason.ConfigurationStale, "stale"));
        });
        using var worker = CreateWorker(queue, processor, configuration);

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            queue.Enqueue(new LibraryWorkItem(Key, LibraryWorkReason.Updated, 1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Phase6Wait.UntilAsync(() => processor.Calls == 1 && queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 1);

            Assert.Equal(WorkHintEnqueueOutcome.InFlight, preReleaseOutcome);
            Assert.Equal(0, queue.Count);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ASameVersionStaleClassificationDoesNotRecur()
    {
        // The bound is implicit in the version the fresh item carries: an item
        // already at the current version can never be re-enqueued for a stale
        // classification, so even a misclassified discard cannot loop. The
        // second scripted outcome completes rather than discarding again, so a
        // broken bound is observed as an extra processing pass.
        using var queue = new LibraryWorkQueue(8);
        var configuration = new ConfigurationSnapshotService(ValidConfiguration());
        Assert.Equal(1, configuration.Current.ConfigurationVersion);

        var calls = 0;
        var processor = new StaleBasisRecordingProcessor((_, _) =>
            Task.FromResult(Interlocked.Increment(ref calls) == 1
                ? WorkProcessingResult.Discarded(DiscardReason.ConfigurationStale, "stale")
                : WorkProcessingResult.Completed()));
        using var worker = CreateWorker(queue, processor, configuration);

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            queue.Enqueue(new LibraryWorkItem(Key, LibraryWorkReason.Updated, 1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Phase6Wait.UntilAsync(() => processor.Calls == 1 && queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 1);

            Assert.Equal(0, queue.Count);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(DiscardReason.ItemMissing)]
    [InlineData(DiscardReason.Ineligible)]
    [InlineData(DiscardReason.ConnectionChanged)]
    [InlineData(DiscardReason.IdentityUnavailable)]
    public async Task NonVersionDiscardsDoNotReenqueue(DiscardReason discardReason)
    {
        using var queue = new LibraryWorkQueue(8);
        var configuration = new ConfigurationSnapshotService(ValidConfiguration());
        Assert.True(configuration.TryReplace(ValidConfiguration(), out _));

        // The item carries the pre-save version, so a version-based decision would
        // be eligible to re-enqueue; only the ConfigurationStale classification is.
        var processor = new StaleBasisRecordingProcessor((_, _) =>
            Task.FromResult(WorkProcessingResult.Discarded(discardReason, "discarded")));
        using var worker = CreateWorker(queue, processor, configuration);

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            queue.Enqueue(new LibraryWorkItem(Key, LibraryWorkReason.Updated, 1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Phase6Wait.UntilAsync(() => processor.Calls == 1 && queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 1);

            Assert.Equal(0, queue.Count);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AStoppedQueueDropsTheStaleBasisReenqueue()
    {
        using var queue = new LibraryWorkQueue(8);
        var configuration = new ConfigurationSnapshotService(ValidConfiguration());
        Assert.True(configuration.TryReplace(ValidConfiguration(), out _));

        var processor = new StaleBasisRecordingProcessor((_, _) =>
        {
            // The queue stops accepting (host shutdown or a lifecycle fence)
            // while the stale item is being processed.
            queue.StopAccepting();
            return Task.FromResult(WorkProcessingResult.Discarded(DiscardReason.ConfigurationStale, "stale"));
        });
        using var worker = CreateWorker(queue, processor, configuration);

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            queue.Enqueue(new LibraryWorkItem(Key, LibraryWorkReason.Updated, 1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Phase6Wait.UntilAsync(() => processor.Calls == 1 && queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 1);

            Assert.False(queue.IsAccepting);
            Assert.Equal(0, queue.Count);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AFullQueueDropsTheStaleBasisReenqueue()
    {
        using var queue = new LibraryWorkQueue(1);
        var configuration = new ConfigurationSnapshotService(ValidConfiguration());
        Assert.True(configuration.TryReplace(ValidConfiguration(), out _));

        var otherKey = new WorkItemKey(Guid.NewGuid(), null, ArtworkImageSurface.Primary);
        var processor = new StaleBasisRecordingProcessor((item, _) =>
        {
            if (item.Key == otherKey)
            {
                return Task.FromResult(WorkProcessingResult.Completed());
            }

            // Fill the bounded pending capacity while the stale item is in
            // flight, so the stale-basis re-enqueue hits the same bound as any
            // other trigger and is dropped rather than blocking.
            Assert.Equal(
                WorkHintEnqueueOutcome.Accepted,
                queue.Enqueue(new LibraryWorkItem(otherKey, LibraryWorkReason.Reconciliation, 2)));
            return Task.FromResult(WorkProcessingResult.Discarded(DiscardReason.ConfigurationStale, "stale"));
        });
        using var worker = CreateWorker(queue, processor, configuration);

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            queue.Enqueue(new LibraryWorkItem(Key, LibraryWorkReason.Updated, 1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // The stale item is processed once; the filler is then dequeued and
            // processed, and no second stale item appears.
            await Phase6Wait.UntilAsync(() => processor.Calls == 2 && queue.Count == 0 && queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 2);

            Assert.Single(processor.Items, item => item.Key == Key);
            Assert.Single(processor.Items, item => item.Key == otherKey);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AStaleBasisDiscardReenqueuesAndReRendersThePublishedPoster()
    {
        // The full F6 scenario over the production Phase 6 graph: the item has
        // published artwork under version 1, the save activates an
        // output-affecting version 2, and the outstanding version-1 work is the
        // only work (the post-save hint it did reach was coalesced). The stale
        // discard must re-enqueue at version 2 and re-render.
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();

        await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);
        Assert.Equal(1, harness.Host.SaveCalls);
        Assert.Equal(1, harness.Renderer.Calls);
        var firstFingerprint = harness.States.Read(harness.ItemId, Phase6Harness.Surface).Value!.PublishedFingerprint;

        Assert.True(harness.Configuration.TryReplace(ReplacementConfiguration(), out _));
        var savedVersion = harness.Configuration.Current.ConfigurationVersion;
        Assert.Equal(2, savedVersion);

        var processor = new StaleBasisRecordingProcessor(
            (item, cancellationToken) => harness.Recovering.ProcessAsync(item, cancellationToken));
        using var worker = harness.CreateWorker(processor, workerCount: 1);

        Assert.Equal(
            WorkHintEnqueueOutcome.Accepted,
            harness.Queue.Enqueue(new LibraryWorkItem(
                new WorkItemKey(harness.ItemId, null, Phase6Harness.Surface),
                LibraryWorkReason.Updated,
                savedVersion - 1)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Phase6Wait.UntilAsync(
                () => harness.Renderer.Calls == 2 && harness.Queue.Count == 0 && harness.Queue.InFlightCount == 0);
            await AssertStableAsync(() => processor.Calls, 2);

            Assert.Equal(new long[] { savedVersion - 1, savedVersion }, processor.Items.Select(item => item.ConfigurationVersion).ToArray());
            Assert.Equal(2, harness.Host.SaveCalls);
            Assert.Contains(
                harness.Renderer.LastRequest!.BadgeDefinitions,
                definition => definition.Selector == BadgeSelector.Quality && definition.Template == "Q{value}");

            var secondFingerprint = harness.States.Read(harness.ItemId, Phase6Harness.Surface).Value!.PublishedFingerprint;
            Assert.NotNull(secondFingerprint);
            Assert.NotEqual(firstFingerprint, secondFingerprint);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void DiscardedCarriesTheClassificationAndStaysCompletedAndNonRetryable()
    {
        var discard = WorkProcessingResult.Discarded(DiscardReason.ItemMissing, "gone");

        Assert.True(discard.IsSuccess);
        Assert.True(discard.IsDiscard);
        Assert.Equal(DiscardReason.ItemMissing, discard.DiscardReason);
        Assert.False(discard.IsRetryable);

        // A non-discard result carries no classification and never re-enqueues.
        Assert.False(WorkProcessingResult.Completed("ok").IsDiscard);
        Assert.Null(WorkProcessingResult.Completed("ok").DiscardReason);
        Assert.Null(WorkProcessingResult.Terminal("no").DiscardReason);
        Assert.Null(WorkProcessingResult.Transient("later").DiscardReason);

        // Guard: an unknown classification cannot be constructed.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WorkProcessingResult.Discarded((DiscardReason)987, "unknown"));
    }

    private static LibraryWorkWorker CreateWorker(
        LibraryWorkQueue queue,
        IWorkItemProcessor processor,
        ConfigurationSnapshotService configuration)
    {
        return new LibraryWorkWorker(
            queue,
            processor,
            configuration,
            workerCount: 1,
            boundedShutdownTimeout: TimeSpan.FromSeconds(2));
    }

    private static PluginConfiguration ValidConfiguration()
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

    private static PluginConfiguration ReplacementConfiguration()
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
            Renderer = new RendererConfiguration
            {
                Selectors = new System.Collections.ObjectModel.Collection<BadgeSelectorConfiguration>
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
    }

    private static async Task AssertStableAsync(Func<int> observe, int expected, int stabilityMilliseconds = 250)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(stabilityMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Assert.Equal(expected, observe());
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A thread-safe processor double that records every processed item and the
    /// peak per-key concurrency, so a test can prove how many fresh items the
    /// worker's stale-basis re-enqueue produced and at which versions. The
    /// recorded item list is bounded; the call count is not, so a broken
    /// re-enqueue bound is observed as a growing call count rather than as
    /// unbounded test-double memory.
    /// </summary>
    private sealed class StaleBasisRecordingProcessor : IWorkItemProcessor
    {
        private const int MaxRecordedItems = 64;

        private readonly object _gate = new();
        private readonly Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>> _handler;
        private readonly List<LibraryWorkItem> _items = new();
        private readonly Dictionary<WorkItemKey, int> _inFlight = new();
        private readonly Dictionary<WorkItemKey, int> _peak = new();
        private int _calls;

        public StaleBasisRecordingProcessor(
            Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
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

        public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _calls++;
                if (_items.Count < MaxRecordedItems)
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
                return await _handler(item, cancellationToken).ConfigureAwait(false);
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
}
