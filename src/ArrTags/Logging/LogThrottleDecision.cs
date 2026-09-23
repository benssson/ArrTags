namespace ArrTags.Logging;

/// <summary>
/// The bounded outcome of one <see cref="LogThrottle"/> admission decision.
/// </summary>
public enum LogThrottleDecision
{
    /// <summary>The message is admitted and should be written.</summary>
    Emit,

    /// <summary>The message is suppressed because the per-window bound is exhausted.</summary>
    Suppress,

    /// <summary>
    /// The window rolled over and previously suppressed messages exist, so one
    /// bounded suppression summary should be written instead.
    /// </summary>
    EmitSuppressionSummary,
}
