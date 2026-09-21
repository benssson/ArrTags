using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Providers;
using ArrTags.State;
using ArrTags.Updates;
using ArrTags.Webhooks;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 duplicate-event coverage: duplicate and out-of-order Jellyfin events
/// and duplicate/replayed webhook deliveries coalesce, the queue stays bounded,
/// and no duplicate render or publication is produced. No live Jellyfin or Arr
/// instance is required.
/// </summary>
public sealed class Phase6DuplicateEventTests
{
    private static readonly ArtworkImageSurface Surface = Phase6Harness.Surface;

    [Fact]
    public async Task DuplicateAndOutOfOrderLibraryEventsCoalesceIntoOnePublication()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();

        var processor = new Phase6RecordingProcessor(
            (item, cancellationToken) => harness.Recovering.ProcessAsync(item, cancellationToken));
        using var worker = harness.CreateWorker(processor, workerCount: 2);

        // Duplicate and out-of-order deliveries for the same subject before any
        // consumer runs.
        Assert.True(harness.Queue.TryEnqueue(harness.Hint(reason: LibraryWorkReason.Added)));
        Assert.False(harness.Queue.TryEnqueue(harness.Hint(reason: LibraryWorkReason.Updated)));
        Assert.False(harness.Queue.TryEnqueue(harness.Hint(reason: LibraryWorkReason.Updated)));
        Assert.Equal(1, harness.Queue.Count);

        await worker.StartAsync(CancellationToken.None);
        await Phase6Wait.UntilAsync(() => processor.Calls == 1 && harness.Queue.InFlightCount == 0);
        await Task.Delay(100);

        Assert.Equal(1, processor.Calls);
        Assert.Equal(1, harness.Renderer.Calls);
        Assert.Equal(1, harness.Host.SaveCalls);
        var key = new WorkItemKey(harness.ItemId, null, Surface);
        Assert.Equal(1, processor.PeakConcurrency(key));

        // A replay after completion reuses the unchanged publication fingerprint:
        // no second render and no second image mutation.
        Assert.True(harness.Queue.TryEnqueue(harness.Hint(reason: LibraryWorkReason.Updated)));
        await Phase6Wait.UntilAsync(() => processor.Calls == 2 && harness.Queue.InFlightCount == 0);
        await Task.Delay(100);

        Assert.Equal(1, harness.Renderer.Calls);
        Assert.Equal(1, harness.Host.SaveCalls);
        Assert.Equal(1, processor.PeakConcurrency(key));

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DuplicateAndReplayedWebhookDeliveriesCoalesceAndBoundedOverflowDrops()
    {
        using var intake = new WebhookIntake(capacity: 4, coalescingWindow: TimeSpan.FromSeconds(5));
        var download = new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42, movieFileId: 84);

        Assert.True(intake.TrySubmit(download));
        Assert.False(intake.TrySubmit(download));
        Assert.False(intake.TrySubmit(new WebhookEvent(
            ArrProviderKind.Radarr,
            WebhookEventType.Download,
            isUpgrade: true,
            movieId: 42,
            movieFileId: 84)));
        Assert.Equal(1, intake.Count);

        // A distinct event kind for the same provider record is admitted.
        Assert.True(intake.TrySubmit(new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Rename, movieId: 42, movieFileId: 84)));
        Assert.Equal(2, intake.Count);

        // Bounded overflow drops without blocking or growing.
        for (var index = 0; index < 50; index++)
        {
            intake.TrySubmit(new WebhookEvent(
                ArrProviderKind.Radarr,
                WebhookEventType.Download,
                movieId: 1000 + index,
                movieFileId: 5000 + index));
        }

        Assert.Equal(4, intake.Capacity);
        Assert.True(intake.Count <= 4);

        // Every dequeued event is distinct: the duplicate Download was suppressed.
        var keys = new List<WebhookCoalesceKey>();
        for (var index = 0; index < 4; index++)
        {
            keys.Add((await intake.DequeueAsync(CancellationToken.None)).CoalesceKey);
        }

        Assert.Equal(4, keys.Distinct().Count());
        Assert.Single(keys, key => key == download.CoalesceKey);

        // No fifth event was admitted.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => intake.DequeueAsync(cancellation.Token));
    }

    [Fact]
    public async Task DuplicateLibraryEventsNeverGrowTheQueueBeyondCapacity()
    {
        using var queue = new LibraryWorkQueue(4);
        var item = Guid.NewGuid();

        Assert.True(queue.TryEnqueue(new LibraryWorkHint(item, LibraryWorkReason.Added, 1)));
        for (var index = 0; index < 100; index++)
        {
            Assert.False(queue.TryEnqueue(new LibraryWorkHint(item, LibraryWorkReason.Updated, 1 + index)));
        }

        Assert.Equal(1, queue.Count);

        // Distinct subjects fill the bounded capacity; overflow is dropped.
        for (var index = 0; index < 25; index++)
        {
            queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1));
        }

        Assert.Equal(4, queue.Count);
        Assert.Equal(4, queue.Capacity);
        await Task.CompletedTask;
    }
}
