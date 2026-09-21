using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.State;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 shutdown coverage for the complete Phase 6 pipeline: graceful
/// shutdown cancels queued and in-flight work, awaits the worker pool within the
/// bounded limit, rejects new work, leaves no unmanaged work, does not publish
/// partial state or a partial image, and coordinates correctly with the lifecycle
/// drain and the artwork publisher fence. No live Jellyfin or Arr instance is
/// required.
/// </summary>
public sealed class Phase6ShutdownTests
{
    private static readonly ArtworkImageSurface Surface = Phase6Harness.Surface;

    [Fact]
    public async Task WorkerShutdownCancelsQueuedAndInFlightWorkWithinTheBound()
    {
        using var harness = new Phase6Harness();
        var processor = new Phase6BlockingProcessor();
        using var worker = harness.CreateWorker(processor, workerCount: 2, shutdownTimeout: TimeSpan.FromSeconds(2));

        await worker.StartAsync(CancellationToken.None);

        // Four distinct subjects fill the queue; the blocking processor holds at
        // most two in flight (the pool size).
        var items = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var item in items)
        {
            Assert.True(harness.Queue.TryEnqueue(harness.Hint(item)));
        }

        await processor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Worker cancellation must be bounded.");
        Assert.False(harness.Queue.IsAccepting);
        Assert.False(harness.Queue.TryEnqueue(harness.Hint(Guid.NewGuid())));

        // The queued work that never started is never started after shutdown, and
        // the in-flight work observed its cancellation.
        var startedAfterStop = processor.Started;
        await Task.Delay(150);
        Assert.Equal(startedAfterStop, processor.Started);
        Assert.Equal(processor.Started, processor.Cancelled);
        Assert.True(processor.Started <= 2, "At most the bounded worker pool may start work.");
        Assert.Equal(0, harness.Queue.InFlightCount);
    }

    [Fact]
    public async Task WorkerShutdownDuringPublicationDoesNotPublishPartialArtwork()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        var original = Phase6Harness.Original();
        harness.Host.CurrentBytes = original;

        // Block the supported image save until the shutdown token fires, so the
        // publication is interrupted in exactly the mutation phase.
        harness.Host.SaveHook = cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken);

        using var worker = harness.CreateWorker(workerCount: 1, shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);

        Assert.True(harness.Queue.TryEnqueue(harness.Hint()));
        await harness.Host.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await worker.StopAsync(CancellationToken.None);

        // Metadata reconciliation completed before the artwork stage.
        Assert.Equal(StateReadStatus.Found, harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Status);

        // The save was attempted but no partial image was applied and no partial
        // final state was published.
        Assert.Equal(1, harness.Host.SaveCalls);
        Assert.Equal(original, harness.Host.CurrentBytes);

        var state = harness.States.Read(harness.ItemId, Surface);
        Assert.True(
            state.Status == StateReadStatus.Missing || state.Value!.State != ArtworkPublicationState.Published,
            "A cancelled publication must not commit a final published state.");

        var operation = harness.Operations.Read(harness.ItemId, Surface).Value!;
        Assert.NotEqual(ArtworkOperationPhase.Committed, operation.Phase);
        Assert.False(harness.Queue.IsAccepting);
    }

    [Fact]
    public async Task ShutdownDrainCoordinatesWithThePublisherFenceAndLeavesNoUntrackedPublication()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        var original = Phase6Harness.Original();
        harness.Host.CurrentBytes = original;

        // Raise the disable fence while the item update is in flight: the image
        // mutation has happened, but the final commit must not cross the fence.
        harness.Host.OnPersistItemUpdate = () => harness.Fences.Set(
            ArtworkLifecycleFence.Disable,
            "The plugin is draining while the publication is in flight.");

        using var worker = harness.CreateWorker(workerCount: 1, shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        Assert.True(harness.Queue.TryEnqueue(harness.Hint()));

        // Wait for the publication to stop at the fence, then stop the worker.
        await Phase6Wait.UntilAsync(
            () => harness.Host.UpdateCalls == 1
                && harness.Queue.InFlightCount == 0
                && harness.Operations.Read(harness.ItemId, Surface).Value is { Phase: ArtworkOperationPhase.FinalizationPending });

        await worker.StopAsync(CancellationToken.None);
        Assert.False(harness.Queue.IsAccepting);

        // The mutation happened, the verified postcondition is durable, but the
        // final state was not committed across the fence.
        var pending = harness.Operations.Read(harness.ItemId, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.FinalizationPending, pending.Phase);
        Assert.False(pending.IsTerminal);
        Assert.Equal(StateReadStatus.Missing, harness.States.Read(harness.ItemId, Surface).Status);

        // The lifecycle drain now recovers the verified postcondition and restores
        // the original, leaving no untracked non-terminal publication.
        var drain = await harness.Lifecycle.DrainAsync(ArtworkLifecycleFence.Disable, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Completed, drain.Outcome);
        Assert.Equal(ArtworkPublicationState.Restored, harness.States.Read(harness.ItemId, Surface).Value!.State);
        Assert.Equal(original, harness.Host.CurrentBytes);
        Assert.DoesNotContain(harness.Operations.Enumerate(10), operation => !operation.IsTerminal);
        Assert.Equal(SourceArtifactReadStatus.Found, harness.Artifacts.Read(
            harness.States.Read(harness.ItemId, Surface).Value!.SourceArtifactId!).Status);
    }

    [Fact]
    public async Task ShutdownCancelsRetryBackoffWithoutStartingAnotherAttempt()
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
        };
        configuration.Limits.TransientRetryCount = 5;
        configuration.Limits.RetryBackoffInitialSeconds = 60;
        configuration.Limits.RetryBackoffFactor = 1;
        configuration.Limits.RetryBackoffMaxSeconds = 120;

        using var harness = new Phase6Harness(configuration: configuration);
        var reads = 0;
        harness.Reader.OnRead = () => Interlocked.Increment(ref reads);
        harness.Reader.Result = Phase6Harness.ProviderUnavailable();

        using var worker = harness.CreateWorker(workerCount: 1, shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        Assert.True(harness.Queue.TryEnqueue(harness.Hint()));

        await Phase6Wait.UntilAsync(() => Volatile.Read(ref reads) >= 1);
        var readsBeforeStop = Volatile.Read(ref reads);

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Cancelling a long retry backoff must be bounded.");
        await Task.Delay(150);
        Assert.Equal(readsBeforeStop, Volatile.Read(ref reads));
        Assert.Equal(1, readsBeforeStop);
        Assert.Equal(0, harness.Queue.InFlightCount);
    }
}
