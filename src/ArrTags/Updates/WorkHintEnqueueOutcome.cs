namespace ArrTags.Updates;

/// <summary>
/// The bounded outcome of one non-blocking enqueue attempt at the
/// <see cref="IWorkHintSink"/> boundary (ADR-022 clause 1). It replaces the
/// single boolean for callers that must distinguish a newly queued hint from a
/// redundant coalesced or in-flight duplicate and from a dropped or refused
/// hint, so a whole-scope reconciliation run can locate the covered prefix
/// instead of stalling on a coalesced duplicate. It is a bounded, secret-free
/// value and is safe to log under the ADR-020 clause 4 redaction contract.
/// </summary>
public enum WorkHintEnqueueOutcome
{
    /// <summary>
    /// The hint was newly queued.
    /// </summary>
    Accepted,

    /// <summary>
    /// The exact key was already pending, so the redundant hint was coalesced
    /// and not queued again.
    /// </summary>
    Coalesced,

    /// <summary>
    /// The exact key was already being processed, so the redundant hint was
    /// coalesced and not queued again.
    /// </summary>
    InFlight,

    /// <summary>
    /// The pending count is at the bounded capacity, so the work was dropped
    /// rather than blocking the caller or growing without bound.
    /// </summary>
    Overflow,

    /// <summary>
    /// The sink is not accepting work through this boundary: the queue has
    /// stopped (the lifecycle fence is raised or the plugin is shutting down),
    /// or the hint is malformed and can never be queued. A whole-scope run
    /// treats this as a stop classification rather than silently skipping the
    /// item.
    /// </summary>
    Stopped,
}
