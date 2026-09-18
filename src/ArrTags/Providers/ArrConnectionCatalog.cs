using System;
using System.Collections.Generic;
using ArrTags.Configuration;

namespace ArrTags.Providers;

/// <summary>
/// Builds the canonical, secret-free connection identities from an immutable
/// configuration snapshot. Both providers are always represented so their
/// independent enablement can be evaluated without external I/O.
/// </summary>
public static class ArrConnectionCatalog
{
    /// <summary>
    /// Creates the canonical connections for the configured providers.
    /// </summary>
    /// <param name="snapshot">The immutable configuration snapshot.</param>
    /// <returns>The Sonarr and Radarr connections, in that order.</returns>
    /// <exception cref="ArgumentNullException">The snapshot is <see langword="null"/>.</exception>
    public static IReadOnlyList<ArrConnection> FromSnapshot(PluginConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new[]
        {
            Create(
                ArrProviderKind.Sonarr,
                snapshot.SonarrEnabled,
                snapshot.SonarrBaseUrl,
                snapshot.SonarrRequestTimeoutSeconds,
                snapshot.SonarrAllowInsecureTls,
                snapshot.SonarrHasApiKey),
            Create(
                ArrProviderKind.Radarr,
                snapshot.RadarrEnabled,
                snapshot.RadarrBaseUrl,
                snapshot.RadarrRequestTimeoutSeconds,
                snapshot.RadarrAllowInsecureTls,
                snapshot.RadarrHasApiKey),
        };
    }

    private static ArrConnection Create(
        ArrProviderKind kind,
        bool enabled,
        string baseUrl,
        int requestTimeoutSeconds,
        bool allowInsecureTls,
        bool hasApiKey)
    {
        var normalizedBaseUrl = baseUrl ?? string.Empty;
        var connectionId = ArrConnectionId.For(kind, normalizedBaseUrl);
        var provider = new ArrProvider(kind, connectionId.Value);

        return new ArrConnection(
            connectionId,
            provider,
            normalizedBaseUrl,
            enabled,
            requestTimeoutSeconds,
            allowInsecureTls ? ArrTlsPolicy.AllowInsecure : ArrTlsPolicy.Strict,
            hasApiKey,
            ArrConnectionHealth.Unknown,
            null);
    }
}
