using System;
using ArrTags.Configuration;

namespace ArrTags.Updates;

/// <summary>
/// The bounded transient-retry policy applied by the Phase 6 worker. It reuses
/// the ADR-004 <see cref="OperationalLimits"/> retry count and backoff values
/// and mirrors the exponential backoff already used at the provider read
/// boundary so queue-level retries never become an unbounded loop.
/// </summary>
public static class WorkRetryPolicy
{
    /// <summary>
    /// Computes the maximum number of processing attempts, including the first
    /// attempt. A configured transient retry count of zero means one attempt and
    /// therefore no retry.
    /// </summary>
    /// <param name="limits">The validated operational limits.</param>
    /// <returns>The bounded attempt count, always at least one.</returns>
    /// <exception cref="ArgumentNullException">The limits are <see langword="null"/>.</exception>
    public static int ComputeAttemptCount(OperationalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return limits.TransientRetryCount < 0 ? 1 : limits.TransientRetryCount + 1;
    }

    /// <summary>
    /// Computes the bounded delay before the retry that follows the supplied
    /// zero-based attempt index.
    /// </summary>
    /// <param name="limits">The validated operational limits.</param>
    /// <param name="attemptIndex">The zero-based index of the attempt that just failed.</param>
    /// <returns>The bounded backoff delay.</returns>
    /// <exception cref="ArgumentNullException">The limits are <see langword="null"/>.</exception>
    public static TimeSpan ComputeBackoff(OperationalLimits limits, int attemptIndex)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (attemptIndex < 0)
        {
            attemptIndex = 0;
        }

        var seconds = limits.RetryBackoffInitialSeconds * Math.Pow(limits.RetryBackoffFactor, attemptIndex);
        return TimeSpan.FromSeconds(Math.Min(seconds, limits.RetryBackoffMaxSeconds));
    }
}
