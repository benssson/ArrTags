using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Logging;
using ArrTags.Providers;
using ArrTags.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArrTags.Webhooks;

/// <summary>
/// The hosted consumer for accepted inbound webhooks. It dequeues bounded events
/// off the request path, resolves them to the Jellyfin items ArrTags already
/// associated with the advertised provider record, and enqueues the same
/// bounded, deduplicated <see cref="LibraryWorkHint"/> work as every other
/// trigger. It never publishes metadata, never mutates artwork, never calls a
/// provider write endpoint, and never blocks a Jellyfin request. On shutdown it
/// stops accepting, cancels, and awaits the loop within a bounded timeout
/// (ADR-012).
/// </summary>
public sealed class WebhookIntakeService : IHostedService, IDisposable
{
    /// <summary>
    /// The bounded time a graceful shutdown is allowed to spend awaiting the
    /// resolution loop before the host stops waiting.
    /// </summary>
    public static readonly TimeSpan BoundedShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly WebhookIntake _intake;
    private readonly WebhookReconciliationResolver _resolver;
    private readonly IWorkHintSink _workHints;
    private readonly ConfigurationSnapshotService _configuration;
    private readonly IArrTagsLog<WebhookIntakeService>? _log;
    private readonly ArrInventoryCacheProvider? _inventory;
    private readonly TimeSpan _shutdownTimeout;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _started;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookIntakeService"/> class.
    /// </summary>
    /// <param name="intake">The bounded coalescing webhook intake.</param>
    /// <param name="resolver">The bounded provider-record-to-Jellyfin resolver.</param>
    /// <param name="workHints">The existing bounded update work hint sink.</param>
    /// <param name="configuration">The current public configuration snapshot.</param>
    /// <param name="boundedShutdownTimeout">The optional bounded shutdown timeout.</param>
    /// <param name="log">The optional bounded, secret-free webhook-boundary log.</param>
    /// <param name="inventory">The optional bounded provider inventory cache (ADR-018); when present, an accepted event invalidates the event's connection so its work begins a fresh provider-read window.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public WebhookIntakeService(
        WebhookIntake intake,
        WebhookReconciliationResolver resolver,
        IWorkHintSink workHints,
        ConfigurationSnapshotService configuration,
        TimeSpan? boundedShutdownTimeout = null,
        IArrTagsLog<WebhookIntakeService>? log = null,
        ArrInventoryCacheProvider? inventory = null)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _workHints = workHints ?? throw new ArgumentNullException(nameof(workHints));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _shutdownTimeout = boundedShutdownTimeout ?? BoundedShutdownTimeout;
        _log = log;
        _inventory = inventory;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        _intake.StartAccepting();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _loop = RunAsync(cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        _intake.StopAccepting();

        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        var loop = Interlocked.Exchange(ref _loop, null);

        try
        {
            if (loop is not null)
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bounded.CancelAfter(_shutdownTimeout);
                await loop.WaitAsync(bounded.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled or timed-out loop wait is bounded and safe; the
            // authoritative periodic reconciliation repairs any missed hint.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The host must still be allowed to shut down.
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _intake.StopAccepting();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            WebhookEvent webhookEvent;
            try
            {
                webhookEvent = await _intake.DequeueAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // An intake failure must never take down the loop or the host.
                if (_log is not null && _log.IsEnabled(LogLevel.Warning))
                {
                    _log.Write(
                        LogLevel.Warning,
                        ArrTagsLogEvent.WebhookEventContained,
                        FormattableString.Invariant($"The webhook intake could not dequeue an event. {exception.GetType().Name}."));
                }

                continue;
            }

            try
            {
                ResolveAndEnqueue(webhookEvent);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A webhook is a best-effort accelerator; a resolution failure is
                // contained and repaired by the authoritative reconciliation.
                if (_log is not null && _log.IsEnabled(LogLevel.Warning))
                {
                    _log.Write(
                        LogLevel.Warning,
                        ArrTagsLogEvent.WebhookEventContained,
                        FormattableString.Invariant($"The webhook event could not be resolved to item hints. {exception.GetType().Name}."));
                }
            }
        }
    }

    private void ResolveAndEnqueue(WebhookEvent webhookEvent)
    {
        var snapshot = _configuration.Current;
        var itemIds = _resolver.Resolve(webhookEvent, snapshot);

        // ADR-018 clause 3: an accepted provider webhook is an ArrTags-side
        // invalidation source. Invalidate before any hint is enqueued so the
        // work this event produces begins a fresh provider-read window instead of
        // serving an observation set taken before the event. The invalidation is
        // scoped to the event's provider/connection where resolvable and is a
        // bounded invalidate-all otherwise; it is best-effort and never blocks or
        // throws into the request or the loop.
        InvalidateInventory(webhookEvent, snapshot);

        for (var index = 0; index < itemIds.Count; index++)
        {
            var hint = new LibraryWorkHint(itemIds[index], LibraryWorkReason.Updated, snapshot.ConfigurationVersion);
            _workHints.TryEnqueue(in hint);
        }

        if (_log is not null && _log.IsEnabled(LogLevel.Debug))
        {
            // Only the bounded event kind, the provider family, and the resolved
            // item count are emitted; the event's advertised provider identifiers
            // and the raw payload are not written (ADR-020 clause 4).
            _log.Write(
                LogLevel.Debug,
                ArrTagsLogEvent.WebhookEventResolved,
                FormattableString.Invariant(
                    $"Webhook event {webhookEvent.EventType} ({webhookEvent.ProviderKind.ToApiName()}) resolved to {itemIds.Count} item hint(s)."));
        }
    }

    /// <summary>
    /// Discards the inventory observation set the accepted event affects
    /// (ADR-018 clause 3). The set is scoped to the event's provider/connection
    /// when the connection can be resolved from the current snapshot, and every
    /// retained set is discarded as a bounded fallback otherwise. It is
    /// best-effort: a failure is contained and repaired by the authoritative
    /// reconciliation, and it never blocks or throws into the Jellyfin request.
    /// </summary>
    private void InvalidateInventory(WebhookEvent webhookEvent, PluginConfigurationSnapshot snapshot)
    {
        if (_inventory is null)
        {
            return;
        }

        try
        {
            var connection = ResolveConnection(snapshot, webhookEvent.ProviderKind);
            if (connection is not null)
            {
                _inventory.Invalidate(connection.ConnectionId);
            }
            else
            {
                _inventory.InvalidateAll();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A webhook is a best-effort accelerator; an invalidation failure is
            // contained and repaired by the authoritative reconciliation.
        }
    }

    private static ArrConnection? ResolveConnection(PluginConfigurationSnapshot snapshot, ArrProviderKind kind)
    {
        foreach (var connection in ArrConnectionCatalog.FromSnapshot(snapshot))
        {
            if (connection.Provider.Kind == kind)
            {
                return connection;
            }
        }

        return null;
    }
}
