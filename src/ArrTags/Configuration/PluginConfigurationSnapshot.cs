using System;
using System.Collections.Generic;

namespace ArrTags.Configuration;

/// <summary>
/// An immutable, secret-free view of a validated <see cref="PluginConfiguration"/>.
/// Background workers receive this snapshot instead of the mutable persisted
/// configuration. Secret values and webhook secrets are intentionally absent.
/// </summary>
public sealed class PluginConfigurationSnapshot
{
    private PluginConfigurationSnapshot(
        bool sonarrEnabled,
        string sonarrBaseUrl,
        int sonarrRequestTimeoutSeconds,
        bool sonarrAllowInsecureTls,
        bool sonarrHasApiKey,
        bool radarrEnabled,
        string radarrBaseUrl,
        int radarrRequestTimeoutSeconds,
        bool radarrAllowInsecureTls,
        bool radarrHasApiKey,
        bool webhookConfigured,
        IReadOnlyList<string> enabledLibraries,
        bool badgeMoviePosters,
        bool badgeEpisodePosters,
        OperationalLimits limits)
    {
        SonarrEnabled = sonarrEnabled;
        SonarrBaseUrl = sonarrBaseUrl;
        SonarrRequestTimeoutSeconds = sonarrRequestTimeoutSeconds;
        SonarrAllowInsecureTls = sonarrAllowInsecureTls;
        SonarrHasApiKey = sonarrHasApiKey;
        RadarrEnabled = radarrEnabled;
        RadarrBaseUrl = radarrBaseUrl;
        RadarrRequestTimeoutSeconds = radarrRequestTimeoutSeconds;
        RadarrAllowInsecureTls = radarrAllowInsecureTls;
        RadarrHasApiKey = radarrHasApiKey;
        WebhookConfigured = webhookConfigured;
        EnabledLibraries = enabledLibraries;
        BadgeMoviePosters = badgeMoviePosters;
        BadgeEpisodePosters = badgeEpisodePosters;
        Limits = limits;
    }

    /// <summary>
    /// Gets a value indicating whether the Sonarr connection is enabled.
    /// </summary>
    public bool SonarrEnabled { get; }

    /// <summary>
    /// Gets the normalized Sonarr base URL.
    /// </summary>
    public string SonarrBaseUrl { get; }

    /// <summary>
    /// Gets the Sonarr request timeout in seconds.
    /// </summary>
    public int SonarrRequestTimeoutSeconds { get; }

    /// <summary>
    /// Gets a value indicating whether insecure TLS is allowed for Sonarr.
    /// </summary>
    public bool SonarrAllowInsecureTls { get; }

    /// <summary>
    /// Gets a value indicating whether a Sonarr API key is configured.
    /// </summary>
    public bool SonarrHasApiKey { get; }

    /// <summary>
    /// Gets a value indicating whether the Radarr connection is enabled.
    /// </summary>
    public bool RadarrEnabled { get; }

    /// <summary>
    /// Gets the normalized Radarr base URL.
    /// </summary>
    public string RadarrBaseUrl { get; }

    /// <summary>
    /// Gets the Radarr request timeout in seconds.
    /// </summary>
    public int RadarrRequestTimeoutSeconds { get; }

    /// <summary>
    /// Gets a value indicating whether insecure TLS is allowed for Radarr.
    /// </summary>
    public bool RadarrAllowInsecureTls { get; }

    /// <summary>
    /// Gets a value indicating whether a Radarr API key is configured.
    /// </summary>
    public bool RadarrHasApiKey { get; }

    /// <summary>
    /// Gets a value indicating whether an inbound webhook secret is configured.
    /// </summary>
    public bool WebhookConfigured { get; }

    /// <summary>
    /// Gets the configured libraries eligible for badges.
    /// </summary>
    public IReadOnlyList<string> EnabledLibraries { get; }

    /// <summary>
    /// Gets a value indicating whether movie posters are eligible.
    /// </summary>
    public bool BadgeMoviePosters { get; }

    /// <summary>
    /// Gets a value indicating whether episode posters are eligible.
    /// </summary>
    public bool BadgeEpisodePosters { get; }

    /// <summary>
    /// Gets the validated operational limits.
    /// </summary>
    public OperationalLimits Limits { get; }

    /// <summary>
    /// Creates a secret-free snapshot from a validated configuration.
    /// </summary>
    /// <param name="configuration">The validated configuration.</param>
    /// <returns>An immutable snapshot.</returns>
    public static PluginConfigurationSnapshot From(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var sonarr = configuration.Sonarr ?? new ArrConnectionConfiguration();
        var radarr = configuration.Radarr ?? new ArrConnectionConfiguration();
        var limits = configuration.Limits ?? new OperationalLimits();
        var libraries = new List<string>(configuration.EnabledLibraries.Count);
        foreach (var library in configuration.EnabledLibraries)
        {
            libraries.Add(library);
        }

        return new PluginConfigurationSnapshot(
            sonarr.Enabled,
            sonarr.BaseUrl ?? string.Empty,
            sonarr.RequestTimeoutSeconds,
            sonarr.AllowInsecureTls,
            !string.IsNullOrEmpty(sonarr.ApiKey),
            radarr.Enabled,
            radarr.BaseUrl ?? string.Empty,
            radarr.RequestTimeoutSeconds,
            radarr.AllowInsecureTls,
            !string.IsNullOrEmpty(radarr.ApiKey),
            !string.IsNullOrEmpty(configuration.WebhookSecret),
            libraries,
            configuration.BadgeMoviePosters,
            configuration.BadgeEpisodePosters,
            limits.Clone());
    }
}
