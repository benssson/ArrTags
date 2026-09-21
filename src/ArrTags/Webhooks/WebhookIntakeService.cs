using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Updates;
using Microsoft.Extensions.Hosting;

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
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public WebhookIntakeService(
        WebhookIntake intake,
        WebhookReconciliationResolver resolver,
        IWorkHintSink workHints,
        ConfigurationSnapshotService configuration,
        TimeSpan? boundedShutdownTimeout = null)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _workHints = workHints ?? throw new ArgumentNullException(nameof(workHints));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _shutdownTimeout = boundedShutdownTimeout ?? BoundedShutdownTimeout;
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
            }
        }
    }

    private void ResolveAndEnqueue(WebhookEvent webhookEvent)
    {
        var snapshot = _configuration.Current;
        var itemIds = _resolver.Resolve(webhookEvent, snapshot);
        for (var index = 0; index < itemIds.Count; index++)
        {
            var hint = new LibraryWorkHint(itemIds[index], LibraryWorkReason.Updated, snapshot.ConfigurationVersion);
            _workHints.TryEnqueue(in hint);
        }
    }
}
