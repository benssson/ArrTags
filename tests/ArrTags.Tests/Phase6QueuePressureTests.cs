using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Media;
using ArrTags.PluginLifecycle;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 queue-pressure coverage: overflow coalesces or drops, the bounded
/// capacity holds under concurrent load, and a slow provider never blocks
/// Jellyfin library event delivery or lets the queue grow without bound. No live
/// Jellyfin or Arr instance is required.
/// </summary>
public sealed class Phase6QueuePressureTests
{
    [Fact]
    public async Task ConcurrentOverflowNeverBlocksAndStaysWithinTheBoundedCapacity()
    {
        using var queue = new LibraryWorkQueue(8);
        var accepted = 0;

        var stopwatch = Stopwatch.StartNew();
        var producers = new List<Task>();
        for (var producer = 0; producer < 8; producer++)
        {
            producers.Add(Task.Run(() =>
            {
                for (var index = 0; index < 500; index++)
                {
                    if (queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)))
                    {
                        Interlocked.Increment(ref accepted);
                    }
                }
            }));
        }

        await Task.WhenAll(producers);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Enqueue must never block the publisher under load.");
        Assert.True(queue.Count <= 8);
        Assert.Equal(8, queue.Capacity);

        // Without a consumer, every accepted hint is still pending and the rest
        // were dropped.
        Assert.Equal(queue.Count, accepted);
    }

    [Fact]
    public async Task SlowProviderWorkNeverBlocksLibraryEventDeliveryAndTheQueueStaysBounded()
    {
        using var harness = new Phase6Harness();
        var processor = new Phase6BlockingProcessor();
        using var worker = harness.CreateWorker(processor, workerCount: 1, shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);

        var events = new FakeLibraryEventSource();
        var lifecycle = new ArrTagsLifecycleService(
            events,
            harness.Lifecycle,
            harness.Configuration,
            harness.Queue);

        await lifecycle.StartAsync(CancellationToken.None);
        try
        {
            // The single worker is stuck on the first subject while thousands of
            // distinct eligible Movie events are delivered synchronously.
            var stopwatch = Stopwatch.StartNew();
            for (var index = 0; index < 2000; index++)
            {
                events.RaiseUpdated(LibraryEventFixtures.Change(
                    Guid.NewGuid(),
                    LibraryWorkReason.Added,
                    MediaItemType.Movie));
            }

            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Library event delivery must not block on the queue.");
            Assert.True(harness.Queue.Count <= harness.Queue.Capacity);
            Assert.InRange(harness.Queue.Count, harness.Queue.Capacity - 1, harness.Queue.Capacity);
            await Phase6Wait.UntilAsync(() => processor.Started >= 1);
            Assert.True(processor.Started >= 1);
        }
        finally
        {
            await lifecycle.StopAsync(CancellationToken.None);
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.False(harness.Queue.IsAccepting);
        Assert.Equal(0, harness.Queue.InFlightCount);
    }

    [Fact]
    public async Task OverflowDuringDrainAcceptsNewWorkOnceCapacityIsFreed()
    {
        var configuration = new ArrTags.Configuration.PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrTags.Configuration.ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
        };
        configuration.Limits.QueueCapacity = 8;

        using var harness = new Phase6Harness(configuration: configuration);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new Phase6RecordingProcessor(async (_, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return WorkProcessingResult.Completed();
        });
        using var worker = harness.CreateWorker(processor, workerCount: 1);
        await worker.StartAsync(CancellationToken.None);

        var capacity = harness.Queue.Capacity;
        var first = Guid.NewGuid();
        Assert.True(harness.Queue.TryEnqueue(harness.Hint(first)));

        // Wait until the first item is in flight, then fill the pending bound.
        await Phase6Wait.UntilAsync(() => harness.Queue.InFlightCount == 1);
        for (var index = 0; index < capacity + 5; index++)
        {
            harness.Queue.TryEnqueue(harness.Hint(Guid.NewGuid()));
        }

        Assert.Equal(capacity, harness.Queue.Count);
        Assert.False(harness.Queue.TryEnqueue(harness.Hint(Guid.NewGuid())));

        // Release the in-flight item and let the worker drain the backlog; a newly
        // freed capacity accepts new work again.
        gate.TrySetResult();
        await Phase6Wait.UntilAsync(() => harness.Queue.InFlightCount == 0 && harness.Queue.Count == 0);
        Assert.True(harness.Queue.TryEnqueue(harness.Hint(Guid.NewGuid())));
        await Phase6Wait.UntilAsync(() => harness.Queue.InFlightCount == 0 && harness.Queue.Count == 0);

        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(capacity + 2, processor.Calls);
    }
}
