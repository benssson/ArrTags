using System.Collections.ObjectModel;
using MediaBrowser.Model.Plugins;

namespace ArrTags.Configuration;

/// <summary>
/// The ArrTags plugin configuration. Updates are treated as replacement
/// snapshots rather than mutable state shared with background workers.
/// </summary>
/// <remarks>
/// Sonarr and Radarr are independently enabled and disabled by default. Secrets
/// are stored here for Jellyfin's configuration persistence but are excluded
/// from canonical snapshots, fingerprints, and diagnostics.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Sonarr connection configuration.
    /// </summary>
    public ArrConnectionConfiguration Sonarr { get; set; } = new ArrConnectionConfiguration();

    /// <summary>
    /// Gets or sets the Radarr connection configuration.
    /// </summary>
    public ArrConnectionConfiguration Radarr { get; set; } = new ArrConnectionConfiguration();

    /// <summary>
    /// Gets or sets the inbound webhook shared secret. The value is never
    /// surfaced in diagnostics.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets the configured libraries eligible for badges. An empty set means no
    /// library restriction.
    /// </summary>
    public Collection<string> EnabledLibraries { get; } = new Collection<string>();

    /// <summary>
    /// Gets or sets a value indicating whether movie posters are eligible.
    /// </summary>
    public bool BadgeMoviePosters { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether episode posters are eligible.
    /// </summary>
    public bool BadgeEpisodePosters { get; set; } = true;

    /// <summary>
    /// Gets or sets the configured operational limits.
    /// </summary>
    public OperationalLimits Limits { get; set; } = new OperationalLimits();

    /// <summary>
    /// Gets or sets the user-adjustable renderer configuration. It contains only
    /// enabled V1 selectors, their bounded templates, and contrast-validated
    /// palette overrides; code-owned output values are not representable here
    /// (ADR-010).
    /// </summary>
    public RendererConfiguration Renderer { get; set; } = new RendererConfiguration();
}
