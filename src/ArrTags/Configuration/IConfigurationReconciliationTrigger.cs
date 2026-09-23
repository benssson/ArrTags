namespace ArrTags.Configuration;

/// <summary>
/// The plugin-owned boundary for requesting a bounded, non-blocking
/// reconciliation after a successful configuration replacement (ADR-016 clause 5
/// second bullet). The contract is deliberately narrow: it accepts no
/// configuration value, item identifier, connection, or secret, so the
/// reconciliation boundary stays host- and provider-neutral.
/// </summary>
/// <remarks>
/// Implementations must return immediately, must never perform library,
/// provider, rendering, or image work on the calling thread, must never block the
/// save response, and must never throw into the host. A redundant request while a
/// reconciliation is already pending is coalesced to at most one bounded rerun
/// rather than queued without bound.
/// </remarks>
public interface IConfigurationReconciliationTrigger
{
    /// <summary>
    /// Requests one bounded reconciliation of the current configured library
    /// scope. The call returns immediately; the reconciliation runs off the
    /// calling thread and enqueues the same bounded, provider-neutral work hints
    /// as the scheduled, manual, and post-scan triggers.
    /// </summary>
    void RequestReconciliation();
}
