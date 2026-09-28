namespace ArrTags.Updates;

/// <summary>
/// The bounded, machine-readable classification of a discarded work item
/// (ADR-023 clause 1). A discard means the reconciliation pipeline did not
/// publish a result for the work item because its basis was no longer valid at
/// the discard point; it is a completed, non-retryable outcome, not a failure.
/// The classification is a bounded, secret-free value and is safe to retain and
/// log under the ADR-020 clause 4 redaction contract.
/// </summary>
public enum DiscardReason
{
    /// <summary>
    /// The work item's carried configuration version is no longer the current
    /// configuration version at the discard point. It covers both the initial
    /// version check and the pre-publication version re-check (ADR-023 clause 1).
    /// </summary>
    ConfigurationStale,

    /// <summary>
    /// The Jellyfin item was no longer present when the work was processed.
    /// </summary>
    ItemMissing,

    /// <summary>
    /// The Jellyfin item was no longer eligible for reconciliation under the
    /// current configuration (for example a disabled badge surface, an item with
    /// no reconciliation provider, or an excluded library/location).
    /// </summary>
    Ineligible,

    /// <summary>
    /// The provider connection was disabled, removed, or replaced while the work
    /// was outstanding.
    /// </summary>
    ConnectionChanged,

    /// <summary>
    /// The item's canonical media identity could no longer be established, or it
    /// no longer matched the identity the work's basis was built from.
    /// </summary>
    IdentityUnavailable,
}
