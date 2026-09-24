using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Logging;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Updates;
using Microsoft.Extensions.Logging;

namespace ArrTags.Reconciliation;

/// <summary>
/// The provider-neutral full-reconciliation trigger. It enumerates the candidate
/// movie and episode items in bounded pages through the media library boundary,
/// filters each page through the canonical identity and eligibility boundary
/// against the current configuration snapshot, and enqueues the same bounded,
/// provider-neutral <see cref="LibraryWorkHint"/> work as every other trigger. It
/// never calls a provider, renderer, publisher, or image API directly; the worker
/// re-reads current Jellyfin and Arr state and discards a changed basis.
/// </summary>
/// <remarks>
/// The enumeration is bounded by <see cref="OperationalLimits.ReconciliationBatchSize"/>:
/// each page holds at most that many candidates, cancellation is checked between
/// pages and inside each page, and the loop yields between pages. The run stops
/// as soon as the durable lifecycle fence refuses new publication work, so a
/// disable or uninstall fence is never crossed. A cancelled or fenced run leaves
/// the durable state authoritative for the next reconciliation.
/// </remarks>
public sealed class LibraryReconciliationService
{
    private readonly ConfigurationSnapshotService _configuration;
    private readonly IMediaLibraryResolver _resolver;
    private readonly IMediaLibraryEnumerator _library;
    private readonly IWorkHintSink _workHints;
    private readonly ArtworkLifecycleFenceStore _fences;
    private readonly IArrTagsLog<LibraryReconciliationService>? _log;
    private readonly ArrInventoryCacheProvider? _inventory;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryReconciliationService"/> class.
    /// </summary>
    /// <param name="configuration">The current public configuration snapshot.</param>
    /// <param name="resolver">The library and item resolver used to build canonical identities.</param>
    /// <param name="library">The bounded candidate enumerator.</param>
    /// <param name="workHints">The bounded, non-blocking enqueue boundary.</param>
    /// <param name="fences">The durable active lifecycle fence store.</param>
    /// <param name="log">The optional bounded, secret-free reconciliation-boundary log.</param>
    /// <param name="inventory">The optional bounded provider inventory cache (ADR-018); when present, a reconciliation invalidates it so the work it enqueues begins a fresh provider-read window.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public LibraryReconciliationService(
        ConfigurationSnapshotService configuration,
        IMediaLibraryResolver resolver,
        IMediaLibraryEnumerator library,
        IWorkHintSink workHints,
        ArtworkLifecycleFenceStore fences,
        IArrTagsLog<LibraryReconciliationService>? log = null,
        ArrInventoryCacheProvider? inventory = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _workHints = workHints ?? throw new ArgumentNullException(nameof(workHints));
        _fences = fences ?? throw new ArgumentNullException(nameof(fences));
        _log = log;
        _inventory = inventory;
    }

    /// <summary>
    /// Runs one bounded reconciliation and enqueues the eligible work hints. The
    /// call never throws for an ordinary provider, library, or cancellation
    /// condition; it returns a bounded result and leaves the durable state
    /// authoritative for the next run.
    /// </summary>
    /// <param name="source">The trigger that requested the reconciliation.</param>
    /// <param name="progress">An optional 0-100 progress sink.</param>
    /// <param name="cancellationToken">The token that cancels the run.</param>
    /// <returns>The bounded reconciliation result.</returns>
    public async Task<LibraryReconciliationResult> ReconcileAsync(
        LibraryReconciliationSource source,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var snapshot = _configuration.Current;

        if (!snapshot.SonarrEnabled && !snapshot.RadarrEnabled)
        {
            return LogResult(LibraryReconciliationResult.Skipped(
                LibraryReconciliationOutcome.NoEnabledProvider,
                source,
                0,
                0,
                0,
                "No Arr provider is enabled, so no reconciliation work is relevant."));
        }

        if (!AllowsNewWork())
        {
            return LogResult(LibraryReconciliationResult.Skipped(
                LibraryReconciliationOutcome.Fenced,
                source,
                0,
                0,
                0,
                "The lifecycle fence refuses new publication work."));
        }

        // ADR-018 clause 3: a reconciliation is an ArrTags-side invalidation
        // source. Post-scan (Jellyfin library refresh), manual and periodic
        // scheduled, and post-save all run through this path, so discarding the
        // retained observation sets here makes the work enqueued below begin a
        // fresh provider-read window instead of serving a set observed before
        // the trigger. The cache is non-authoritative; the next read simply
        // re-populates it. The periodic scheduled reconciliation is not replaced
        // by this: it still runs on its interval and enqueues work as before,
        // with the cache as an accelerator rather than the source of truth.
        InvalidateInventory();

        var batchSize = snapshot.Limits.ReconciliationBatchSize;
        if (batchSize < 1)
        {
            batchSize = 1;
        }

        var total = CountCandidates();
        var inspected = 0;
        var eligible = 0;
        var enqueued = 0;
        var startIndex = 0;

        Report(progress, 0);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!AllowsNewWork())
                {
                    return LogResult(LibraryReconciliationResult.Skipped(
                        LibraryReconciliationOutcome.Fenced,
                        source,
                        inspected,
                        eligible,
                        enqueued,
                        "The lifecycle fence refuses new publication work."));
                }

                var page = _library.EnumerateCandidates(startIndex, batchSize)
                    ?? Array.Empty<MediaBrowser.Controller.Entities.BaseItem>();
                if (page.Count == 0)
                {
                    break;
                }

                foreach (var item in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    inspected++;

                    if (item is null)
                    {
                        continue;
                    }

                    try
                    {
                        if (!TryCreateEligibleHint(item, snapshot, out var hint))
                        {
                            continue;
                        }

                        eligible++;
                        if (_workHints.TryEnqueue(in hint))
                        {
                            enqueued++;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // A single malformed item must not abort the bounded
                        // reconciliation; the next run re-reads current state.
                    }
                }

                startIndex += page.Count;

                if (total > 0)
                {
                    Report(progress, Math.Clamp((double)inspected * 100d / total, 0d, 100d));
                }

                if (!AllowsNewWork())
                {
                    return LogResult(LibraryReconciliationResult.Skipped(
                        LibraryReconciliationOutcome.Fenced,
                        source,
                        inspected,
                        eligible,
                        enqueued,
                        "The lifecycle fence was raised during the reconciliation; no further work was enqueued."));
                }

                if (page.Count < batchSize)
                {
                    break;
                }

                // Yield between batches so the bounded worker can make progress
                // and the scheduler stays responsive.
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            return LogResult(LibraryReconciliationResult.Cancelled(source, inspected, eligible, enqueued));
        }

        Report(progress, 100);
        return LogResult(LibraryReconciliationResult.Completed(
            source,
            inspected,
            eligible,
            enqueued,
            "The bounded reconciliation completed."));
    }

    /// <summary>
    /// Writes one bounded, secret-free reconciliation-boundary record. The
    /// bounded <see cref="LibraryReconciliationResult"/> summary (outcome,
    /// trigger, counts, and reason) carries no item identifier, path, credential,
    /// or provider payload (ADR-020 clause 4).
    /// </summary>
    private LibraryReconciliationResult LogResult(LibraryReconciliationResult result)
    {
        if (_log is null || !_log.IsEnabled(LogLevel.Information))
        {
            return result;
        }

        _log.Write(
            LogLevel.Information,
            result.Outcome == LibraryReconciliationOutcome.Completed
                ? ArrTagsLogEvent.ReconciliationCompleted
                : ArrTagsLogEvent.ReconciliationSkipped,
            FormattableString.Invariant(
                $"Reconciliation ({result.Source}) {result.Outcome}: inspected {result.Inspected}, eligible {result.Eligible}, enqueued {result.Enqueued}. {result.Reason}"));
        return result;
    }

    private bool AllowsNewWork()
    {
        try
        {
            return _fences.Read().AllowsNewPublication;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A fence that cannot be read fails closed rather than enqueueing work.
            return false;
        }
    }

    /// <summary>
    /// Discards every retained provider inventory observation set (ADR-018
    /// clause 3) so the work this reconciliation enqueues begins a fresh
    /// provider-read window. The invalidation is bounded and best-effort: a
    /// failure is contained and never aborts the reconciliation, and the cache is
    /// non-authoritative so the next read simply re-populates it.
    /// </summary>
    private void InvalidateInventory()
    {
        if (_inventory is null)
        {
            return;
        }

        try
        {
            _inventory.InvalidateAll();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Invalidation is an accelerator; the reconciliation remains
            // authoritative for the bounded work it enqueues.
        }
    }

    private int CountCandidates()
    {
        try
        {
            var count = _library.CountCandidates();
            return count < 0 ? 0 : count;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The total is only a progress denominator; a failed count never
            // bounds or blocks the enumeration.
            return 0;
        }
    }

    private bool TryCreateEligibleHint(
        MediaBrowser.Controller.Entities.BaseItem item,
        PluginConfigurationSnapshot snapshot,
        out LibraryWorkHint hint)
    {
        hint = default;

        if (item.Id == Guid.Empty)
        {
            return false;
        }

        if (!MediaIdentityFactory.TryCreate(item, _resolver, out var identity) || identity is null)
        {
            return false;
        }

        if (!MediaEligibility.IsEligible(identity, snapshot))
        {
            return false;
        }

        hint = new LibraryWorkHint(item.Id, LibraryWorkReason.Reconciliation, snapshot.ConfigurationVersion);
        return true;
    }

    private static void Report(IProgress<double>? progress, double value)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress.Report(value);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A progress sink must never affect the reconciliation result.
        }
    }
}
