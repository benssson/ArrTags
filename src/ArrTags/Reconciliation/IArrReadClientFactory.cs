using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;

namespace ArrTags.Reconciliation;

/// <summary>
/// Creates a read client for the current connection at the reconciliation I/O
/// boundary. The connection is resolved from the current configuration snapshot
/// per operation, so a replaced snapshot, a disabled connection, or a rotated
/// credential fences new provider requests without scrubbing queue contents.
/// </summary>
public interface IArrReadClientFactory
{
    /// <summary>
    /// Creates the read client for a Radarr connection.
    /// </summary>
    /// <param name="connection">The resolved Radarr connection.</param>
    /// <returns>The Radarr read client.</returns>
    IRadarrReadClient CreateRadarr(ArrConnection connection);

    /// <summary>
    /// Creates the read client for a Sonarr connection.
    /// </summary>
    /// <param name="connection">The resolved Sonarr connection.</param>
    /// <returns>The Sonarr read client.</returns>
    ISonarrReadClient CreateSonarr(ArrConnection connection);
}
