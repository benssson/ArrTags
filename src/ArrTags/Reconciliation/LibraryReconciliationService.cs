using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Logging;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Updates;
using MediaBrowser.Controller.Entities;
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
/// <para>
/// The scheduled and post-scan whole-scope runs additionally advance the
/// persisted reconciliation cursor (ADR-022 as amended by ADR-029): they resume
/// strictly after the recorded unique <c>itemId</c> anchor by walking the
/// host-ordered pages from the start, wrap at the end of the order, reset to the
/// start when the anchor cannot be located or the scope changed, and advance the
/// cursor only over the covered prefix when the run stops at the saturated queue,
/// the lifecycle fence, or cancellation. An accepted, coalesced, or in-flight
/// enqueue is covered and the run continues; an item that correctly needs no work
/// is covered as well. The bounded post-save prefix re-cover is deliberately
/// excluded (ADR-022 clause 6): it always enumerates from the start and never
/// reads or writes the cursor.
/// </para>
/// </remarks>
public sealed class LibraryReconciliationService
{
    private readonly ConfigurationSnapshotService _configuration;
    private readonly IMediaLibraryResolver _resolver;
    private readonly IMediaLibraryEnumerator _library;
    private readonly IWorkHintSink _workHints;
    private readonly ArtworkLifecycleFenceStore _fences;
    private readonly ReconciliationCursorStore _cursorStore;
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
    /// <param name="cursorStore">The persisted whole-scope reconciliation cursor store.</param>
    /// <param name="log">The optional bounded, secret-free reconciliation-boundary log.</param>
    /// <param name="inventory">The optional bounded provider inventory cache (ADR-018); when present, a reconciliation invalidates it so the work it enqueues begins a fresh provider-read window.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public LibraryReconciliationService(
        ConfigurationSnapshotService configuration,
        IMediaLibraryResolver resolver,
        IMediaLibraryEnumerator library,
        IWorkHintSink workHints,
        ArtworkLifecycleFenceStore fences,
        ReconciliationCursorStore cursorStore,
        IArrTagsLog<LibraryReconciliationService>? log = null,
        ArrInventoryCacheProvider? inventory = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _workHints = workHints ?? throw new ArgumentNullException(nameof(workHints));
        _fences = fences ?? throw new ArgumentNullException(nameof(fences));
        _cursorStore = cursorStore ?? throw new ArgumentNullException(nameof(cursorStore));
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

        // ADR-022 clause 6: the cursor governs only the whole-scope scheduled
        // (periodic or manual) and post-scan triggers. The bounded post-save
        // prefix re-cover and every per-item trigger always enumerate from the
        // start and never read or write the cursor.
        var usesCursor = source is LibraryReconciliationSource.Scheduled or LibraryReconciliationSource.PostScan;
        var scope = usesCursor ? ReconciliationScopeIdentity.From(snapshot) : null;

        var total = CountCandidates();
        var inspected = 0;
        var eligible = 0;
        var enqueued = 0;
        var covered = 0;
        var startIndex = 0;
        var wrapped = false;
        IReadOnlyList<BaseItem>? prefetched = null;
        ReconciliationCursorPosition? lastCovered = null;

        Report(progress, 0);

        try
        {
            if (scope is not null)
            {
                var cursor = ReadCursor(scope);
                if (cursor.Position is { } anchor)
                {
                    var resume = await ResumeAsync(anchor, batchSize, cancellationToken).ConfigureAwait(false);
                    if (resume.Outcome == MediaLibraryResumeOutcome.Located)
                    {
                        startIndex = resume.StartIndex;
                        prefetched = resume.Candidates;
                    }

                    // ADR-029 clause 4: an anchor that cannot be located (it was
                    // removed, became ineligible, or the host order changed)
                    // resets the cursor to the start rather than failing the run.
                }
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!AllowsNewWork())
                {
                    AdvanceCursor(scope, wrapped, lastCovered);
                    return LogResult(LibraryReconciliationResult.Skipped(
                        LibraryReconciliationOutcome.Fenced,
                        source,
                        inspected,
                        eligible,
                        enqueued,
                        covered,
                        "The lifecycle fence refuses new publication work."));
                }

                var page = prefetched
                    ?? _library.EnumerateCandidates(startIndex, batchSize)
                    ?? Array.Empty<BaseItem>();
                prefetched = null;

                if (page.Count == 0)
                {
                    // The candidate order is exhausted (or was empty). The durable
                    // cursor wraps to the start so coverage is round-robin rather
                    // than a one-shot prefix (ADR-022 clause 2).
                    wrapped = true;
                    break;
                }

                WorkHintEnqueueOutcome? stopOutcome = null;
                foreach (var item in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    inspected++;

                    if (item is null)
                    {
                        continue;
                    }

                    var needsWork = false;
                    LibraryWorkHint hint = default;
                    try
                    {
                        needsWork = TryCreateEligibleHint(item, snapshot, out hint);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // A single malformed item is covered by the bookkeeping
                        // below: it needs no work and must not stall the cursor.
                        needsWork = false;
                    }

                    if (needsWork)
                    {
                        eligible++;
                        var outcome = EnqueueHint(in hint);
                        if (outcome == WorkHintEnqueueOutcome.Accepted)
                        {
                            enqueued++;
                        }
                        else if (usesCursor
                            && outcome is WorkHintEnqueueOutcome.Overflow or WorkHintEnqueueOutcome.Stopped)
                        {
                            // ADR-022 clause 2: overflow and a stopped queue end
                            // the whole-scope run at the first uncovered item, so
                            // the cursor advances only over the covered prefix.
                            stopOutcome = outcome;
                            break;
                        }
                    }

                    covered++;
                    if (item.Id != Guid.Empty)
                    {
                        lastCovered = CreateCursorPosition(item);
                    }
                }

                if (stopOutcome is { } stop)
                {
                    AdvanceCursor(scope, wrapped, lastCovered);
                    return stop == WorkHintEnqueueOutcome.Overflow
                        ? LogResult(LibraryReconciliationResult.Skipped(
                            LibraryReconciliationOutcome.QueueSaturated,
                            source,
                            inspected,
                            eligible,
                            enqueued,
                            covered,
                            "The bounded work queue is at capacity; the run stopped at the first uncovered item and the cursor covers the inspected prefix."))
                        : LogResult(LibraryReconciliationResult.Skipped(
                            LibraryReconciliationOutcome.Fenced,
                            source,
                            inspected,
                            eligible,
                            enqueued,
                            covered,
                            "The bounded work queue is not accepting work; the run stopped at the first uncovered item and the cursor covers the inspected prefix."));
                }

                startIndex += page.Count;

                if (total > 0)
                {
                    Report(progress, Math.Clamp((double)inspected * 100d / total, 0d, 100d));
                }

                if (!AllowsNewWork())
                {
                    AdvanceCursor(scope, wrapped, lastCovered);
                    return LogResult(LibraryReconciliationResult.Skipped(
                        LibraryReconciliationOutcome.Fenced,
                        source,
                        inspected,
                        eligible,
                        enqueued,
                        covered,
                        "The lifecycle fence was raised during the reconciliation; no further work was enqueued."));
                }

                if (page.Count < batchSize)
                {
                    // The last page of the host order was covered: the cursor
                    // wraps to the start for the next run.
                    wrapped = true;
                    break;
                }

                // Yield between batches so the bounded worker can make progress
                // and the scheduler stays responsive.
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            // A cancellation advances the cursor only over the items already
            // covered by this run (ADR-022 clause 2).
            AdvanceCursor(scope, wrapped, lastCovered);
            return LogResult(LibraryReconciliationResult.Cancelled(source, inspected, eligible, enqueued, covered));
        }

        AdvanceCursor(scope, wrapped, lastCovered);
        Report(progress, 100);
        return LogResult(LibraryReconciliationResult.Completed(
            source,
            inspected,
            eligible,
            enqueued,
            covered,
            "The bounded reconciliation reached the end of the candidate order."));
    }

    /// <summary>
    /// Reads the durable cursor and never lets a state failure fail the run: the
    /// cursor is rebuildable cache state, so an unreadable record is observed as
    /// a miss and resets the run to the start.
    /// </summary>
    private ReconciliationCursorReadResult ReadCursor(ReconciliationScopeIdentity scope)
    {
        try
        {
            return _cursorStore.Read(scope);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ReconciliationCursorReadResult.Missing();
        }
    }

    /// <summary>
    /// Walks the host-ordered pages to locate the recorded anchor. A library
    /// failure during the locate walk resets the run to the start rather than
    /// failing it (ADR-029 clause 4). Cancellation is propagated so the run
    /// reports the bounded cancelled result.
    /// </summary>
    private async Task<MediaLibraryResumeResult> ResumeAsync(
        ReconciliationCursorPosition anchor,
        int maxItems,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _library
                .ResumeCandidatesAsync(anchor.ItemId, maxItems, cancellationToken)
                .ConfigureAwait(false)
                ?? MediaLibraryResumeResult.AnchorNotFound();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return MediaLibraryResumeResult.AnchorNotFound();
        }
    }

    /// <summary>
    /// Enqueues one bounded hint through the outcome-returning boundary. A sink
    /// failure cannot confirm the work is queued, so it is reported as a dropped
    /// enqueue rather than a covered item; cancellation is propagated.
    /// </summary>
    private WorkHintEnqueueOutcome EnqueueHint(in LibraryWorkHint hint)
    {
        try
        {
            return _workHints.Enqueue(in hint);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return WorkHintEnqueueOutcome.Overflow;
        }
    }

    /// <summary>
    /// Persists the new cursor position for a whole-scope run (ADR-022 clause 2).
    /// A wrap records the start; otherwise the position of the last covered item
    /// is recorded, and only over the covered prefix. A run that covered nothing
    /// and did not wrap leaves the recorded cursor untouched. A failed write never
    /// fails the run or its bounded result.
    /// </summary>
    private void AdvanceCursor(
        ReconciliationScopeIdentity? scope,
        bool wrapped,
        ReconciliationCursorPosition? lastCovered)
    {
        if (scope is null || (!wrapped && lastCovered is null))
        {
            return;
        }

        try
        {
            _cursorStore.Write(scope, wrapped ? null : lastCovered);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The cursor is a rebuildable accelerator; the durable state remains
            // authoritative for the next run.
        }
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
                $"Reconciliation ({result.Source}) {result.Outcome}: inspected {result.Inspected}, eligible {result.Eligible}, enqueued {result.Enqueued}, covered {result.Covered}. {result.Reason}"));
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

    /// <summary>
    /// Builds the durable cursor position for a covered item. The host
    /// <c>SortName</c> component is a non-authoritative boundary hint and
    /// diagnostic (ADR-029 clause 3), so a hint that cannot be read is stored as
    /// empty while the unique item identifier remains the anchor.
    /// </summary>
    private static ReconciliationCursorPosition CreateCursorPosition(BaseItem item)
    {
        string? sortName = null;
        try
        {
            sortName = item.SortName;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The hint is not the ordering authority; the identity anchor is.
        }

        return new ReconciliationCursorPosition(sortName, item.Id);
    }

    private bool TryCreateEligibleHint(
        BaseItem item,
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
