using System;

namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded summary of one scheduled, manual, or post-scan reconciliation.
/// The counts are lower bounds whenever the outcome is
/// <see cref="LibraryReconciliationOutcome.Cancelled"/> or the run stopped at the
/// lifecycle fence. It carries no item identifiers, paths, credentials, or
/// provider payloads, so it is safe to report as a diagnostic.
/// </summary>
public sealed class LibraryReconciliationResult
{
    private LibraryReconciliationResult(
        LibraryReconciliationOutcome outcome,
        LibraryReconciliationSource source,
        int inspected,
        int eligible,
        int enqueued,
        string reason)
    {
        Outcome = outcome;
        Source = source;
        Inspected = inspected;
        Eligible = eligible;
        Enqueued = enqueued;
        Reason = reason;
    }

    /// <summary>
    /// Gets the bounded outcome.
    /// </summary>
    public LibraryReconciliationOutcome Outcome { get; }

    /// <summary>
    /// Gets the trigger that requested the reconciliation.
    /// </summary>
    public LibraryReconciliationSource Source { get; }

    /// <summary>
    /// Gets the number of candidate items inspected through the media library boundary.
    /// </summary>
    public int Inspected { get; }

    /// <summary>
    /// Gets the number of inspected items that were within the configured scope and badge surface.
    /// </summary>
    public int Eligible { get; }

    /// <summary>
    /// Gets the number of eligible items whose bounded hint was accepted by the work queue.
    /// </summary>
    public int Enqueued { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation of the outcome.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Creates a completed result.
    /// </summary>
    /// <param name="source">The trigger that requested the reconciliation.</param>
    /// <param name="inspected">The number of inspected candidates.</param>
    /// <param name="eligible">The number of eligible candidates.</param>
    /// <param name="enqueued">The number of accepted hints.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>The completed result.</returns>
    public static LibraryReconciliationResult Completed(
        LibraryReconciliationSource source,
        int inspected,
        int eligible,
        int enqueued,
        string reason)
    {
        return new LibraryReconciliationResult(
            LibraryReconciliationOutcome.Completed,
            source,
            inspected,
            eligible,
            enqueued,
            reason);
    }

    /// <summary>
    /// Creates a skipped result for a run that enqueued no work.
    /// </summary>
    /// <param name="outcome">The non-completed outcome.</param>
    /// <param name="source">The trigger that requested the reconciliation.</param>
    /// <param name="inspected">The number of inspected candidates.</param>
    /// <param name="eligible">The number of eligible candidates.</param>
    /// <param name="enqueued">The number of accepted hints.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>The skipped result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is <see cref="LibraryReconciliationOutcome.Completed"/>.</exception>
    public static LibraryReconciliationResult Skipped(
        LibraryReconciliationOutcome outcome,
        LibraryReconciliationSource source,
        int inspected,
        int eligible,
        int enqueued,
        string reason)
    {
        if (outcome == LibraryReconciliationOutcome.Completed)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "A skipped result cannot be completed.");
        }

        return new LibraryReconciliationResult(outcome, source, inspected, eligible, enqueued, reason);
    }

    /// <summary>
    /// Creates a cancelled result.
    /// </summary>
    /// <param name="source">The trigger that requested the reconciliation.</param>
    /// <param name="inspected">The number of inspected candidates.</param>
    /// <param name="eligible">The number of eligible candidates.</param>
    /// <param name="enqueued">The number of accepted hints.</param>
    /// <returns>The cancelled result.</returns>
    public static LibraryReconciliationResult Cancelled(
        LibraryReconciliationSource source,
        int inspected,
        int eligible,
        int enqueued)
    {
        return new LibraryReconciliationResult(
            LibraryReconciliationOutcome.Cancelled,
            source,
            inspected,
            eligible,
            enqueued,
            "The reconciliation was cancelled; the durable state remains authoritative for the next run.");
    }
}
