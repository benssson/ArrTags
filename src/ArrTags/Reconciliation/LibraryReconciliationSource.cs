namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded source that requested a full reconciliation. It is a safe,
/// provider-neutral diagnostic value; manual execution reuses the scheduled task
/// surface, so a manual run and a periodic run are both reported as
/// <see cref="Scheduled"/>.
/// </summary>
public enum LibraryReconciliationSource
{
    /// <summary>A periodic or manually started scheduled reconciliation.</summary>
    Scheduled,

    /// <summary>A reconciliation requested after a Jellyfin library scan.</summary>
    PostScan,

    /// <summary>
    /// A bounded reconciliation requested after a successful configuration
    /// replacement, so saved settings re-render existing posters without waiting
    /// for the next library event, webhook, post-scan, or scheduled run
    /// (ADR-016 clause 5 second bullet).
    /// </summary>
    PostSave,
}
