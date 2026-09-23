using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Logging;
using ArrTags.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The ArrTags hosted lifecycle. It owns library-event subscription for the
/// duration of its lifetime and performs no provider, rendering, or full-library
/// work during registration or startup. Its library-event handlers are short and
/// synchronous: they validate relevance against the current public configuration
/// snapshot, enqueue a bounded provider-neutral work hint, and return without
/// external I/O, rendering, or image writes. On a graceful shutdown it
/// establishes a bounded disable/uninstall drain through the lifecycle
/// coordinator, and it re-reads an <c>ItemRemoved</c> hint on a tracked, bounded
/// background task so synchronous library event delivery is never blocked.
/// </summary>
public sealed class ArrTagsLifecycleService : IHostedService, IDisposable
{
    /// <summary>
    /// The bounded time a graceful shutdown is allowed to spend draining
    /// lifecycle work before the host stops waiting.
    /// </summary>
    public static readonly TimeSpan BoundedDrainTimeout = TimeSpan.FromSeconds(15);

    private readonly ILibraryEventSource _libraryEvents;
    private readonly IArtworkLifecycleCoordinator _coordinator;
    private readonly ConfigurationSnapshotService _configuration;
    private readonly IWorkHintSink _workHints;
    private readonly IArrTagsLog<ArrTagsLifecycleService>? _log;
    private readonly TimeSpan _boundedDrainTimeout;
    private readonly ConcurrentDictionary<Task, byte> _pendingRemovals = new();
    private int _subscribed;
    private int _stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrTagsLifecycleService"/> class.
    /// </summary>
    /// <param name="libraryEvents">The library event source to observe.</param>
    /// <param name="coordinator">The lifecycle coordinator that drains fences and handles item removal.</param>
    /// <param name="configuration">The current public configuration snapshot used for synchronous relevance checks.</param>
    /// <param name="workHints">The bounded, non-blocking enqueue boundary for relevant update work.</param>
    /// <param name="boundedDrainTimeout">An optional bounded drain timeout; defaults to <see cref="BoundedDrainTimeout"/>.</param>
    /// <param name="log">The optional bounded, secret-free lifecycle-boundary log.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArrTagsLifecycleService(
        ILibraryEventSource libraryEvents,
        IArtworkLifecycleCoordinator coordinator,
        ConfigurationSnapshotService configuration,
        IWorkHintSink workHints,
        TimeSpan? boundedDrainTimeout = null,
        IArrTagsLog<ArrTagsLifecycleService>? log = null)
    {
        _libraryEvents = libraryEvents ?? throw new ArgumentNullException(nameof(libraryEvents));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _workHints = workHints ?? throw new ArgumentNullException(nameof(workHints));
        _boundedDrainTimeout = boundedDrainTimeout ?? BoundedDrainTimeout;
        _log = log;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The host loaded the plugin active, so a previous disable or uninstall
        // fence did not take effect and must not lock out future publication. The
        // per-subject state and operation records still guard unsafe work.
        _coordinator.ResetStaleFence();

        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            _libraryEvents.ItemAdded += OnLibraryItemChanged;
            _libraryEvents.ItemUpdated += OnLibraryItemChanged;
            _libraryEvents.ItemRemoved += OnLibraryItemChanged;
        }

        if (_log is not null && _log.IsEnabled(LogLevel.Information))
        {
            _log.Write(
                LogLevel.Information,
                ArrTagsLogEvent.LifecycleStarted,
                "The ArrTags lifecycle started and library events are subscribed.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        Unsubscribe();

        if (_log is not null && _log.IsEnabled(LogLevel.Information))
        {
            _log.Write(
                LogLevel.Information,
                ArrTagsLogEvent.LifecycleStopping,
                "The ArrTags lifecycle is stopping and beginning its bounded drain.");
        }

        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(_boundedDrainTimeout);
            await _coordinator.DrainForHostShutdownAsync(bounded.Token).ConfigureAwait(false);
            await AwaitPendingRemovalsAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled or timed-out drain is bounded and safe: the durable
            // fence and recovery records remain for the next reconciliation.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The host must still be allowed to shut down; the durable fence and
            // journal remain authoritative.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unsubscribe();
    }

    private void OnLibraryItemChanged(object? sender, LibraryItemChangedEventArgs e)
    {
        if (Volatile.Read(ref _stopping) != 0 || e.ItemId == Guid.Empty)
        {
            return;
        }

        // An item removal additionally preserves the task 5.9 durable lifecycle
        // semantics: a tracked, bounded background confirmation and tombstone.
        // It stays off the library-event thread.
        if (e.Reason == LibraryWorkReason.Removed)
        {
            if (_log is not null && _log.IsEnabled(LogLevel.Debug))
            {
                _log.Write(
                    LogLevel.Debug,
                    ArrTagsLogEvent.LifecycleItemRemoved,
                    FormattableString.Invariant(
                        $"Library item {e.ItemId:D} was removed; a bounded artwork removal was requested."));
            }

            TrackRemoval(e.ItemId);
        }

        try
        {
            var snapshot = _configuration.Current;
            if (LibraryEventRelevance.TryCreateHint(e, snapshot, out var hint))
            {
                _workHints.TryEnqueue(in hint);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A library-event handler must never surface a failure to the host's
            // event publisher; the next reconciliation repairs missed work.
        }
    }

    private void TrackRemoval(Guid itemId)
    {
        var task = TrackRemovalAsync(itemId);
        _pendingRemovals.TryAdd(task, 0);
        _ = task.ContinueWith(
            static (completed, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(completed, out _),
            _pendingRemovals,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task TrackRemovalAsync(Guid itemId)
    {
        try
        {
            using var bounded = new CancellationTokenSource(_boundedDrainTimeout);
            await _coordinator
                .HandleItemRemovedAsync(itemId, ArtworkImageSurface.Primary, bounded.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A bounded item-removal confirmation may be safely abandoned.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A bounded item-removal confirmation must never surface to the host.
        }
    }

    private async Task AwaitPendingRemovalsAsync(CancellationToken cancellationToken)
    {
        var pending = _pendingRemovals.Keys.ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The bounded shutdown wait elapsed; outstanding confirmations are
            // abandoned without affecting the host.
        }
    }

    private void Unsubscribe()
    {
        if (Interlocked.Exchange(ref _subscribed, 0) == 0)
        {
            return;
        }

        _libraryEvents.ItemAdded -= OnLibraryItemChanged;
        _libraryEvents.ItemUpdated -= OnLibraryItemChanged;
        _libraryEvents.ItemRemoved -= OnLibraryItemChanged;
    }
}
