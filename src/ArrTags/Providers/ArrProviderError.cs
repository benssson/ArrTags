using System;

namespace ArrTags.Providers;

/// <summary>
/// A bounded, redacted provider failure. Messages are truncated and must never
/// contain API keys, request URLs, or raw provider payloads.
/// </summary>
public sealed class ArrProviderError
{
    /// <summary>
    /// The maximum retained diagnostic message length.
    /// </summary>
    public const int MaxMessageLength = 512;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrProviderError"/> class.
    /// </summary>
    /// <param name="code">The stable error code.</param>
    /// <param name="retryability">The retry classification.</param>
    /// <param name="message">An optional bounded, non-secret diagnostic message.</param>
    /// <exception cref="ArgumentOutOfRangeException">A classification value is not defined.</exception>
    public ArrProviderError(ArrProviderErrorCode code, ArrErrorRetryability retryability, string? message)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown provider error code.");
        }

        if (!Enum.IsDefined(retryability))
        {
            throw new ArgumentOutOfRangeException(nameof(retryability), retryability, "Unknown retry classification.");
        }

        Code = code;
        Retryability = retryability;
        Message = Bound(message);
    }

    /// <summary>
    /// Gets the stable error code.
    /// </summary>
    public ArrProviderErrorCode Code { get; }

    /// <summary>
    /// Gets the retry classification.
    /// </summary>
    public ArrErrorRetryability Retryability { get; }

    /// <summary>
    /// Gets the bounded, non-secret diagnostic message when present.
    /// </summary>
    public string? Message { get; }

    private static string? Bound(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var trimmed = message.Trim();
        return trimmed.Length <= MaxMessageLength ? trimmed : trimmed[..MaxMessageLength];
    }
}
