namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded outcome of reading the persisted reconciliation cursor
/// (ADR-022 clause 5). A missing, discarded, or scope-changed record resets the
/// run to the start of the candidate order; a wrapped record is the recorded
/// start; a position record supplies the identity anchor the run resumes after.
/// </summary>
public enum ReconciliationCursorStatus
{
    /// <summary>
    /// No cursor record exists. The run starts at the beginning of the order.
    /// </summary>
    Missing,

    /// <summary>
    /// An invalid or torn cache record was discarded by the repository. The
    /// cursor resets to the start rather than failing the run.
    /// </summary>
    Discarded,

    /// <summary>
    /// A valid record exists for a different scope (a changed enabled-library or
    /// item-type surface scope), so the cursor resets to the start.
    /// </summary>
    ScopeChanged,

    /// <summary>
    /// A valid record for the current scope records the wrapped start of the
    /// candidate order. The run starts at the beginning.
    /// </summary>
    Wrapped,

    /// <summary>
    /// A valid record for the current scope supplies the opaque
    /// <c>(SortName, itemId)</c> position of the last covered candidate.
    /// </summary>
    Position,
}
