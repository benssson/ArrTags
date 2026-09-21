using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused tests for the task 6.2 bounded, coalescing, cancellation-aware work
/// queue and hosted worker: coalescing by item and connection, single-flight,
/// worker cancellation during processing and backoff, retry classification and
/// bounds, queue overflow, no-blocking enqueue, and safe diagnostics. They
/// require no live Jellyfin or Arr instance.
/// </summary>
public class LibraryWorkQueueTests
{
    [Fact]
    public void CoalescesRedundantHintsForTheSameItem()
    {
        using var queue = new LibraryWorkQueue(8);
        var itemId = Guid.NewGuid();

        Assert.True(queue.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Added, 1)));
        Assert.False(queue.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 2)));
        Assert.Equal(1, queue.Count);
        Assert.Equal(0, queue.InFlightCount);
    }

    [Fact]
    public void CoalescesByItemAndConnectionAndKeepsDistinctKeys()
    {
        using var queue = new LibraryWorkQueue(8);
        var itemId = Guid.NewGuid();
        var sonarr = new WorkItemKey(itemId, "sonarr:http://sonarr.test", ArtworkImageSurface.Primary);
        var radarr = new WorkItemKey(itemId, "radarr:http://radarr.test", ArtworkImageSurface.Primary);
        var alternateSurface = new WorkItemKey(
            itemId,
            "sonarr:http://sonarr.test",
            new ArtworkImageSurface(ArtworkImageType.Primary, 1));

        Assert.True(queue.TryEnqueue(new LibraryWorkItem(sonarr, LibraryWorkReason.Added, 1)));

        // Same item and connection coalesces, even with a newer reason/version.
        Assert.False(queue.TryEnqueue(new LibraryWorkItem(sonarr, LibraryWorkReason.Updated, 2)));

        // A different connection or surface is a distinct key.
        Assert.True(queue.TryEnqueue(new LibraryWorkItem(radarr, LibraryWorkReason.Added, 1)));
        Assert.True(queue.TryEnqueue(new LibraryWorkItem(alternateSurface, LibraryWorkReason.Added, 1)));
        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public async Task CoalescesRedundantHintWhileWorkIsInFlight()
    {
        using var queue = new LibraryWorkQueue(8);
        var itemId = Guid.NewGuid();
        var hint = new LibraryWorkHint(itemId, LibraryWorkReason.Added, 1);

        Assert.True(queue.TryEnqueue(hint));

        var inFlight = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(itemId, inFlight.Key.ItemId);
        Assert.Equal(1, queue.InFlightCount);

        // Single-flight: a redundant hint for in-flight work is coalesced.
        Assert.False(queue.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 2)));
        Assert.Equal(0, queue.Count);
        Assert.Equal(1, queue.InFlightCount);

        queue.CompleteProcessing(inFlight.Key);
        Assert.Equal(0, queue.InFlightCount);

        // Once the in-flight work completes, the item can be enqueued again.
        Assert.True(queue.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 2)));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void RejectsAnEmptyItemIdWithoutThrowing()
    {
        using var queue = new LibraryWorkQueue(4);

        Assert.False(queue.TryEnqueue(default(LibraryWorkHint)));
        Assert.False(queue.TryEnqueue(new LibraryWorkItem(default, LibraryWorkReason.Added, 1)));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void DropsOverflowWithoutBlockingOrThrowing()
    {
        using var queue = new LibraryWorkQueue(2);

        var stopwatch = Stopwatch.StartNew();
        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        Assert.False(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        stopwatch.Stop();

        Assert.Equal(2, queue.Capacity);
        Assert.Equal(2, queue.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Overflow must never block the caller.");
    }

    [Fact]
    public async Task DequeuesInOrderAndReleasesCapacity()
    {
        using var queue = new LibraryWorkQueue(1);
        var first = new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1);
        var second = new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1);

        Assert.True(queue.TryEnqueue(first));
        Assert.False(queue.TryEnqueue(second));

        var dequeued = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(first.ItemId, dequeued.Key.ItemId);
        queue.CompleteProcessing(dequeued.Key);

        Assert.True(queue.TryEnqueue(second));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task DiagnosticsReportBoundedPendingAndInFlightCounts()
    {
        using var queue = new LibraryWorkQueue(2);

        Assert.Equal(2, queue.Capacity);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.InFlightCount);

        var first = new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1);
        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        Assert.Equal(2, queue.Count);
        Assert.Equal(0, queue.InFlightCount);

        var dequeued = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal(1, queue.Count);
        Assert.Equal(1, queue.InFlightCount);

        queue.CompleteProcessing(dequeued.Key);
        Assert.Equal(0, queue.InFlightCount);
    }

    [Fact]
    public async Task WorkerProcessesACoalescedItemExactlyOnce()
    {
        using var queue = new LibraryWorkQueue(8);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new RecordingWorkProcessor(async (_, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return WorkProcessingResult.Completed();
        });
        using var worker = CreateWorker(queue, processor, workerCount: 4);
        await worker.StartAsync(CancellationToken.None);

        var itemId = Guid.NewGuid();
        Assert.True(queue.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Added, 1)));
        await WaitUntilAsync(() => processor.CallCount == 1);

        // A redundant hint while the first is in flight must not start a second
        // concurrent processing of the same item and surface.
        Assert.False(queue.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 2)));

        gate.TrySetResult(true);
        await WaitUntilAsync(() => queue.InFlightCount == 0);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, processor.CallCount);
        var key = WorkItemKey.FromHint(new LibraryWorkHint(itemId, LibraryWorkReason.Added, 1));
        Assert.Equal(1, processor.MaxConcurrencyFor(key));
    }

    [Fact]
    public async Task WorkerRetriesATransientOutcomeThenCompletes()
    {
        using var queue = new LibraryWorkQueue(8);
        var processor = new RecordingWorkProcessor(
            WorkProcessingResult.Transient("temporary"),
            WorkProcessingResult.Completed());
        using var worker = CreateWorker(queue, processor, workerCount: 1, configureLimits: FastBackoff(retries: 2));
        await worker.StartAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        await WaitUntilAsync(() => processor.CallCount == 2);
        await WaitUntilAsync(() => queue.InFlightCount == 0);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, processor.CallCount);
    }

    [Fact]
    public async Task WorkerDoesNotRetryATerminalOutcome()
    {
        using var queue = new LibraryWorkQueue(8);
        var processor = new RecordingWorkProcessor(WorkProcessingResult.Terminal("terminal"));
        using var worker = CreateWorker(queue, processor, workerCount: 1, configureLimits: FastBackoff(retries: 3));
        await worker.StartAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        await WaitUntilAsync(() => queue.InFlightCount == 0 && processor.CallCount == 1);
        await Task.Delay(250);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public async Task WorkerAbandonsRetriesAfterTheBoundedAttemptCount()
    {
        using var queue = new LibraryWorkQueue(8);
        var processor = new RecordingWorkProcessor(WorkProcessingResult.Transient("temporary"));
        using var worker = CreateWorker(queue, processor, workerCount: 1, configureLimits: FastBackoff(retries: 1));
        await worker.StartAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));

        // TransientRetryCount of one means two attempts total, then abandonment.
        await WaitUntilAsync(() => processor.CallCount == 2);
        await Task.Delay(500);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, processor.CallCount);
    }

    [Fact]
    public async Task StopCancelsInFlightProcessing()
    {
        using var queue = new LibraryWorkQueue(8);
        var processor = new RecordingWorkProcessor(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return WorkProcessingResult.Completed();
        });
        using var worker = CreateWorker(queue, processor, workerCount: 2);
        await worker.StartAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        await WaitUntilAsync(() => processor.CallCount == 1);

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.False(queue.IsAccepting);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Worker cancellation must be bounded.");
    }

    [Fact]
    public async Task StopCancelsRetryBackoff()
    {
        using var queue = new LibraryWorkQueue(8);
        var processor = new RecordingWorkProcessor(WorkProcessingResult.Transient("temporary"));
        using var worker = CreateWorker(
            queue,
            processor,
            workerCount: 1,
            configureLimits: limits =>
            {
                limits.TransientRetryCount = 5;
                limits.RetryBackoffInitialSeconds = 60;
                limits.RetryBackoffFactor = 1;
                limits.RetryBackoffMaxSeconds = 120;
            });
        await worker.StartAsync(CancellationToken.None);

        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        await WaitUntilAsync(() => processor.CallCount == 1);

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // The worker is cancelled during the long backoff, so a second attempt is
        // never started.
        Assert.Equal(1, processor.CallCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Retry backoff cancellation must be bounded.");
    }

    [Fact]
    public async Task StopRejectsNewWorkAndStartAcceptsItAgain()
    {
        using var queue = new LibraryWorkQueue(8);
        var processor = new RecordingWorkProcessor(WorkProcessingResult.Completed());
        using var worker = CreateWorker(queue, processor, workerCount: 1);

        await worker.StartAsync(CancellationToken.None);
        Assert.True(queue.IsAccepting);

        await worker.StopAsync(CancellationToken.None);
        Assert.False(queue.IsAccepting);
        Assert.False(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));

        await worker.StartAsync(CancellationToken.None);
        Assert.True(queue.IsAccepting);
        Assert.True(queue.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        await WaitUntilAsync(() => processor.CallCount == 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public void RetryPolicyComputesBoundedAttemptsAndBackoff()
    {
        var limits = new OperationalLimits
        {
            TransientRetryCount = 2,
            RetryBackoffInitialSeconds = 1,
            RetryBackoffFactor = 2,
            RetryBackoffMaxSeconds = 5,
        };

        Assert.Equal(3, WorkRetryPolicy.ComputeAttemptCount(limits));
        Assert.Equal(TimeSpan.FromSeconds(1), WorkRetryPolicy.ComputeBackoff(limits, 0));
        Assert.Equal(TimeSpan.FromSeconds(2), WorkRetryPolicy.ComputeBackoff(limits, 1));
        Assert.Equal(TimeSpan.FromSeconds(4), WorkRetryPolicy.ComputeBackoff(limits, 2));
        Assert.Equal(TimeSpan.FromSeconds(5), WorkRetryPolicy.ComputeBackoff(limits, 10));

        limits.TransientRetryCount = 0;
        Assert.Equal(1, WorkRetryPolicy.ComputeAttemptCount(limits));
    }

    [Fact]
    public void ProcessingResultReusesProviderRetryClassification()
    {
        Assert.True(WorkProcessingResult.Completed().IsSuccess);
        Assert.False(WorkProcessingResult.Completed().IsRetryable);

        Assert.True(WorkProcessingResult.Transient("temporary").IsRetryable);
        Assert.False(WorkProcessingResult.Terminal("terminal").IsRetryable);

        Assert.True(WorkProcessingResult.FromRetryability(ArrErrorRetryability.Later).IsRetryable);
        Assert.False(WorkProcessingResult.FromRetryability(ArrErrorRetryability.Never).IsRetryable);
        Assert.False(WorkProcessingResult.FromRetryability(ArrErrorRetryability.AfterConfiguration).IsRetryable);
    }

    private static LibraryWorkWorker CreateWorker(
        LibraryWorkQueue queue,
        IWorkItemProcessor processor,
        int workerCount,
        Action<OperationalLimits>? configureLimits = null)
    {
        var configuration = new PluginConfiguration();
        configureLimits?.Invoke(configuration.Limits);
        var snapshot = new ConfigurationSnapshotService(configuration);
        return new LibraryWorkWorker(queue, processor, snapshot, workerCount);
    }

    private static Action<OperationalLimits> FastBackoff(int retries)
    {
        return limits =>
        {
            limits.TransientRetryCount = retries;
            limits.RetryBackoffInitialSeconds = 1;
            limits.RetryBackoffFactor = 1;
            limits.RetryBackoffMaxSeconds = 1;
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected condition was not observed within the bounded test timeout.");
    }

    private sealed class RecordingWorkProcessor : IWorkItemProcessor
    {
        private readonly object _gate = new object();
        private readonly Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>>? _handler;
        private readonly WorkProcessingResult[]? _scripted;
        private readonly Dictionary<WorkItemKey, int> _inFlightByKey = new Dictionary<WorkItemKey, int>();
        private readonly Dictionary<WorkItemKey, int> _maxConcurrencyByKey = new Dictionary<WorkItemKey, int>();
        private int _inFlight;

        public RecordingWorkProcessor(Func<LibraryWorkItem, CancellationToken, Task<WorkProcessingResult>> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public RecordingWorkProcessor(params WorkProcessingResult[] scripted)
        {
            _scripted = scripted ?? throw new ArgumentNullException(nameof(scripted));
            if (scripted.Length == 0)
            {
                throw new ArgumentException("At least one scripted result is required.", nameof(scripted));
            }
        }

        public int CallCount { get; private set; }

        public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
        {
            int callIndex;
            lock (_gate)
            {
                callIndex = CallCount;
                CallCount++;
                _inFlight++;
                _inFlightByKey.TryGetValue(item.Key, out var perKey);
                perKey++;
                _inFlightByKey[item.Key] = perKey;
                _maxConcurrencyByKey.TryGetValue(item.Key, out var maxPerKey);
                if (perKey > maxPerKey)
                {
                    _maxConcurrencyByKey[item.Key] = perKey;
                }
            }

            try
            {
                if (_scripted is not null)
                {
                    var index = Math.Min(callIndex, _scripted.Length - 1);
                    return _scripted[index];
                }

                return await _handler!(item, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight--;
                    _inFlightByKey[item.Key] = _inFlightByKey[item.Key] - 1;
                }
            }
        }

        public int MaxConcurrencyFor(WorkItemKey key)
        {
            lock (_gate)
            {
                return _maxConcurrencyByKey.TryGetValue(key, out var value) ? value : 0;
            }
        }
    }
}
