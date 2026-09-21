using System;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using ArrTags.Secrets;

namespace ArrTags.Reconciliation;

/// <summary>
/// Creates concrete read clients from Jellyfin's pooled transport for the current
/// connection. Operational limits are resolved from the current configuration
/// snapshot at creation time, matching the task 6.2 precedent, so a replaced
/// snapshot takes effect without rebuilding the singleton.
/// </summary>
public sealed class ArrReadClientFactory : IArrReadClientFactory
{
    private readonly IArrHttpClientFactory _httpClientFactory;
    private readonly IPluginSecretResolver _secrets;
    private readonly ConfigurationSnapshotService _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrReadClientFactory"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The secret-free pooled transport factory.</param>
    /// <param name="secrets">The versioned credential boundary.</param>
    /// <param name="configuration">The configuration snapshot service supplying current limits.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public ArrReadClientFactory(
        IArrHttpClientFactory httpClientFactory,
        IPluginSecretResolver secrets,
        ConfigurationSnapshotService configuration)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public IRadarrReadClient CreateRadarr(ArrConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new RadarrClient(connection, _httpClientFactory, _secrets, _configuration.Current.Limits);
    }

    /// <inheritdoc />
    public ISonarrReadClient CreateSonarr(ArrConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new SonarrClient(connection, _httpClientFactory, _secrets, _configuration.Current.Limits);
    }
}
