namespace ArrTags.Providers;

/// <summary>
/// Stable provider-boundary error codes. Consumers branch on the code, never on
/// provider error text.
/// </summary>
public enum ArrProviderErrorCode
{
    /// <summary>
    /// The provider is unreachable or returned a transient failure.
    /// </summary>
    ProviderUnavailable,

    /// <summary>
    /// The provider rejected the configured credentials.
    /// </summary>
    AuthenticationFailed,

    /// <summary>
    /// The provider version, contract, or required field shape is unsupported.
    /// </summary>
    ProviderIncompatible,

    /// <summary>
    /// The provider response was empty, malformed, or not JSON.
    /// </summary>
    InvalidResponse,
}
