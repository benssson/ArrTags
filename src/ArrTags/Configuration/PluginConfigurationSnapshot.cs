using System;
using System.Collections.Generic;
using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// An immutable, secret-free view of a validated <see cref="PluginConfiguration"/>.
/// Background workers receive this snapshot instead of the mutable persisted
/// configuration. Secret values and webhook secrets are intentionally absent.
/// </summary>
public sealed class PluginConfigurationSnapshot
{
    private PluginConfigurationSnapshot(
        long configurationVersion,
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
        OperationalLimits limits,
        IReadOnlyList<BadgeDefinition> badgeDefinitions,
        RenderOutputPolicy rendererOutputPolicy,
        string rendererConfigurationFingerprint)
    {
        ConfigurationVersion = configurationVersion;
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
        BadgeDefinitions = badgeDefinitions;
        RendererOutputPolicy = rendererOutputPolicy;
        RendererConfigurationFingerprint = rendererConfigurationFingerprint;
    }

    /// <summary>
    /// Gets the monotonic configuration generation. It increments when a valid
    /// replacement is activated and fences credential leases to one generation.
    /// </summary>
    public long ConfigurationVersion { get; }

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
    /// Gets the ordered, provider-neutral badge definitions resolved from the
    /// validated renderer configuration. A selector absent from configuration
    /// keeps the code-owned ADR-009 default, so the default snapshot equals
    /// <see cref="BadgeDefinition.V1Default"/>.
    /// </summary>
    public IReadOnlyList<BadgeDefinition> BadgeDefinitions { get; }

    /// <summary>
    /// Gets the effective renderer output policy. The configured palette is
    /// applied while every code-owned format, color-space, alpha, font, geometry,
    /// text, and version value is preserved.
    /// </summary>
    public RenderOutputPolicy RendererOutputPolicy { get; }

    /// <summary>
    /// Gets the secret-free, deterministic renderer configuration fingerprint
    /// over the selector enablement/templates and the effective palette. It
    /// excludes credentials, the webhook secret, timestamps, and correlation
    /// identifiers, and is the value supplied to
    /// <see cref="RenderRequest.ConfigurationFingerprint"/>.
    /// </summary>
    public string RendererConfigurationFingerprint { get; }

    /// <summary>
    /// Creates a secret-free snapshot from a validated configuration.
    /// </summary>
    /// <param name="configuration">The validated configuration.</param>
    /// <param name="configurationVersion">The configuration generation to stamp.</param>
    /// <returns>An immutable snapshot.</returns>
    public static PluginConfigurationSnapshot From(PluginConfiguration configuration, long configurationVersion = 1)
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

        var renderer = configuration.Renderer ?? new RendererConfiguration();
        var badgeDefinitions = RendererConfigurationResolver.ResolveDefinitions(renderer);
        var rendererOutputPolicy = RendererConfigurationResolver.ResolveOutputPolicy(renderer);
        var rendererConfigurationFingerprint = RendererConfigurationResolver.ComputeFingerprint(
            badgeDefinitions,
            rendererOutputPolicy);

        return new PluginConfigurationSnapshot(
            configurationVersion,
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
            limits.Clone(),
            badgeDefinitions,
            rendererOutputPolicy,
            rendererConfigurationFingerprint);
    }
}
