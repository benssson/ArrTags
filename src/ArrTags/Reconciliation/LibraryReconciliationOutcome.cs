namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded outcome of one full reconciliation request. It never carries an
/// item identifier, path, credential, or provider payload.
/// </summary>
public enum LibraryReconciliationOutcome
{
    /// <summary>The bounded enumeration completed.</summary>
    Completed,

    /// <summary>No Arr provider was enabled, so no work was relevant.</summary>
    NoEnabledProvider,

    /// <summary>The lifecycle fence refused new work, so no work was enqueued.</summary>
    Fenced,

    /// <summary>
    /// The bounded pending queue was saturated, so the whole-scope run stopped at
    /// the first uncovered item and the cursor covers the inspected prefix
    /// (ADR-022 clause 2).
    /// </summary>
    QueueSaturated,

    /// <summary>The reconciliation was cancelled before it completed.</summary>
    Cancelled,
}
