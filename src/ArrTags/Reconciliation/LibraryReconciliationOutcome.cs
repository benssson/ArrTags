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

    /// <summary>The reconciliation was cancelled before it completed.</summary>
    Cancelled,
}
