using System;
using ArrTags.Providers;

namespace ArrTags.Updates;

/// <summary>
/// The classified result of processing one <see cref="LibraryWorkItem"/>. A
/// worker retries only a transient (<see cref="ArrErrorRetryability.Later"/>)
/// outcome, within the bounded ADR-004 retry count and backoff; a terminal
/// outcome is never retried. The optional reason is bounded and must never
/// contain a credential or an unbounded provider payload.
/// </summary>
public sealed class WorkProcessingResult
{
    /// <summary>
    /// The maximum retained diagnostic reason length.
    /// </summary>
    public const int MaxReasonLength = 512;

    private WorkProcessingResult(bool isSuccess, ArrErrorRetryability retryability, string? reason)
    {
        if (!Enum.IsDefined(retryability))
        {
            throw new ArgumentOutOfRangeException(nameof(retryability), retryability, "Unknown retry classification.");
        }

        IsSuccess = isSuccess;
        Retryability = retryability;
        Reason = Bound(reason);
    }

    /// <summary>
    /// Gets a value indicating whether the work item was processed successfully.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the retry classification of the outcome.
    /// </summary>
    public ArrErrorRetryability Retryability { get; }

    /// <summary>
    /// Gets the bounded, non-secret diagnostic reason when present.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets a value indicating whether the outcome should be retried later.
    /// </summary>
    public bool IsRetryable => !IsSuccess && Retryability == ArrErrorRetryability.Later;

    /// <summary>
    /// Creates a successful outcome. No retry is performed.
    /// </summary>
    /// <param name="reason">An optional bounded, non-secret reason.</param>
    /// <returns>The successful outcome.</returns>
    public static WorkProcessingResult Completed(string? reason = null)
    {
        return new WorkProcessingResult(true, ArrErrorRetryability.Never, reason);
    }

    /// <summary>
    /// Creates a transient outcome that may be retried later within the bounded
    /// retry policy.
    /// </summary>
    /// <param name="reason">An optional bounded, non-secret reason.</param>
    /// <returns>The transient outcome.</returns>
    public static WorkProcessingResult Transient(string? reason = null)
    {
        return new WorkProcessingResult(false, ArrErrorRetryability.Later, reason);
    }

    /// <summary>
    /// Creates a terminal outcome that must not be retried without a change.
    /// </summary>
    /// <param name="reason">An optional bounded, non-secret reason.</param>
    /// <returns>The terminal outcome.</returns>
    public static WorkProcessingResult Terminal(string? reason = null)
    {
        return new WorkProcessingResult(false, ArrErrorRetryability.Never, reason);
    }

    /// <summary>
    /// Reuses the provider retry classification to create a failed outcome. A
    /// <see cref="ArrErrorRetryability.Later"/> classification is retryable;
    /// every other classification is terminal.
    /// </summary>
    /// <param name="retryability">The provider-boundary retry classification.</param>
    /// <param name="reason">An optional bounded, non-secret reason.</param>
    /// <returns>The classified outcome.</returns>
    public static WorkProcessingResult FromRetryability(ArrErrorRetryability retryability, string? reason = null)
    {
        return new WorkProcessingResult(false, retryability, reason);
    }

    private static string? Bound(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var trimmed = reason.Trim();
        return trimmed.Length <= MaxReasonLength ? trimmed : trimmed[..MaxReasonLength];
    }
}
