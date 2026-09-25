namespace ArrTags.Providers;

/// <summary>
/// The single bounded mapping from a redacted provider error code to the
/// observed <see cref="ArrConnectionHealth"/>. It classifies only the stable
/// error codes the provider boundary produces; every other code is unclassified
/// rather than guessed. The mapping exposes no provider payload, path, item
/// name, or credential.
/// </summary>
public static class ArrConnectionHealthMapping
{
    /// <summary>
    /// Classifies a bounded provider error code into the observed connection
    /// health.
    /// </summary>
    /// <param name="code">The bounded provider error code.</param>
    /// <returns>The observed connection health.</returns>
    public static ArrConnectionHealth FromErrorCode(ArrProviderErrorCode code)
    {
        return code switch
        {
            ArrProviderErrorCode.AuthenticationFailed => ArrConnectionHealth.AuthenticationFailed,
            ArrProviderErrorCode.ProviderUnavailable => ArrConnectionHealth.Unavailable,
            ArrProviderErrorCode.ProviderIncompatible => ArrConnectionHealth.Incompatible,
            _ => ArrConnectionHealth.Unknown,
        };
    }
}
