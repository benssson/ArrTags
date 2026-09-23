namespace ArrTags.Logging;

/// <summary>
/// The bounded, code-owned set of ArrTags log call sites (ADR-020 clause 4). Each
/// value is a stable event identity used as the repetition-suppression key
/// (ADR-020 clause 6) and as the host <c>EventId</c>; the set is finite, so the
/// log-volume bound cannot be driven by unbounded caller-supplied keys. A log
/// message is never identified by free text.
/// </summary>
public enum ArrTagsLogEvent
{
    /// <summary>No event. Never emitted; present only to give the enum a defined zero value.</summary>
    None = 0,

    /// <summary>A provider-boundary read completed successfully.</summary>
    ProviderReadSucceeded = 1001,

    /// <summary>A provider-boundary read failed with a bounded <c>ArrProviderError</c>.</summary>
    ProviderReadFailed = 1002,

    /// <summary>The matching boundary resolved a canonical match outcome.</summary>
    MatchResolved = 1101,

    /// <summary>The metadata boundary published a canonical metadata state.</summary>
    MetadataPublished = 1201,

    /// <summary>The metadata boundary discarded a work item with a bounded reason.</summary>
    MetadataDiscarded = 1202,

    /// <summary>The metadata boundary observed a bounded provider read failure.</summary>
    MetadataReadFailed = 1203,

    /// <summary>The artwork boundary completed a generation attempt with a bounded outcome.</summary>
    ArtworkGenerationCompleted = 1301,

    /// <summary>The queue boundary processed one work item with a bounded outcome.</summary>
    WorkItemProcessed = 1401,

    /// <summary>The queue boundary scheduled a bounded retry for a transient outcome.</summary>
    WorkItemRetryScheduled = 1402,

    /// <summary>The queue boundary contained an unexpected processor or queue failure.</summary>
    WorkItemFailed = 1403,

    /// <summary>The reconciliation boundary completed a bounded run.</summary>
    ReconciliationCompleted = 1501,

    /// <summary>The reconciliation boundary skipped a bounded run.</summary>
    ReconciliationSkipped = 1502,

    /// <summary>The webhook boundary rejected an unauthenticated request.</summary>
    WebhookAuthenticationRejected = 1601,

    /// <summary>The webhook boundary resolved an authenticated event to item hints.</summary>
    WebhookEventResolved = 1602,

    /// <summary>The webhook boundary contained an event-resolution failure.</summary>
    WebhookEventContained = 1603,

    /// <summary>The lifecycle boundary started.</summary>
    LifecycleStarted = 1701,

    /// <summary>The lifecycle boundary began a bounded shutdown drain.</summary>
    LifecycleStopping = 1702,

    /// <summary>The lifecycle boundary observed an item removal.</summary>
    LifecycleItemRemoved = 1703,
}
