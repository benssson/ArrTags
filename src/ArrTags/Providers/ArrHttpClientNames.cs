using System;

namespace ArrTags.Providers;

/// <summary>
/// The stable names of the Arr HTTP clients registered with Jellyfin's standard
/// <c>IHttpClientFactory</c>. One name exists per provider kind and TLS policy so
/// the pooled message handler always matches the connection's certificate policy.
/// </summary>
public static class ArrHttpClientNames
{
    /// <summary>
    /// The strict Sonarr client name.
    /// </summary>
    public const string Sonarr = "ArrTags.Sonarr";

    /// <summary>
    /// The strict Radarr client name.
    /// </summary>
    public const string Radarr = "ArrTags.Radarr";

    /// <summary>
    /// The suffix appended to a name for a connection that allows insecure TLS.
    /// </summary>
    public const string InsecureSuffix = ".Insecure";

    /// <summary>
    /// Gets the registered client name for a provider kind and TLS policy.
    /// </summary>
    /// <param name="kind">The provider kind.</param>
    /// <param name="tlsPolicy">The connection certificate policy.</param>
    /// <returns>The stable named-client name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is not defined.</exception>
    public static string For(ArrProviderKind kind, ArrTlsPolicy tlsPolicy)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Arr provider kind.");
        }

        if (!Enum.IsDefined(tlsPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(tlsPolicy), tlsPolicy, "Unknown TLS policy.");
        }

        var name = kind == ArrProviderKind.Sonarr ? Sonarr : Radarr;
        return tlsPolicy == ArrTlsPolicy.AllowInsecure ? name + InsecureSuffix : name;
    }
}
