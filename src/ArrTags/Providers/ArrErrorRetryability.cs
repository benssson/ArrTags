namespace ArrTags.Providers;

/// <summary>
/// Classifies whether and when a bounded provider operation may be retried so
/// callers do not enter a rapid retry loop.
/// </summary>
public enum ArrErrorRetryability
{
    /// <summary>
    /// The operation must not be retried without a change.
    /// </summary>
    Never,

    /// <summary>
    /// The operation may be retried later within the bounded retry policy.
    /// </summary>
    Later,

    /// <summary>
    /// The operation may be retried only after the connection configuration is
    /// corrected.
    /// </summary>
    AfterConfiguration,
}
