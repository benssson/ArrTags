using System;

namespace ArrTags.Configuration;

/// <summary>
/// Owns the active configuration snapshot and replaces it only when a candidate
/// configuration validates. An invalid replacement leaves the previous snapshot
/// active.
/// </summary>
public sealed class ConfigurationSnapshotService
{
    private readonly object _gate = new object();
    private PluginConfigurationSnapshot _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationSnapshotService"/> class
    /// with the default configuration.
    /// </summary>
    public ConfigurationSnapshotService()
        : this(new PluginConfiguration())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationSnapshotService"/> class
    /// from a candidate configuration. An invalid candidate falls back to the
    /// default configuration so invalid input cannot bring down Jellyfin.
    /// </summary>
    /// <param name="configuration">The initial candidate configuration.</param>
    public ConfigurationSnapshotService(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var result = PluginConfigurationValidator.Validate(configuration);
        _current = result.IsValid
            ? PluginConfigurationSnapshot.From(configuration)
            : PluginConfigurationSnapshot.From(new PluginConfiguration());
    }

    /// <summary>
    /// Gets the active configuration snapshot.
    /// </summary>
    public PluginConfigurationSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Attempts to validate and activate a replacement configuration snapshot.
    /// The previous snapshot remains active when validation fails.
    /// </summary>
    /// <param name="configuration">The candidate configuration.</param>
    /// <param name="result">The safe validation result.</param>
    /// <returns><see langword="true"/> when the replacement was activated.</returns>
    public bool TryReplace(PluginConfiguration configuration, out ConfigurationValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        result = PluginConfigurationValidator.Validate(configuration);
        if (!result.IsValid)
        {
            return false;
        }

        var snapshot = PluginConfigurationSnapshot.From(configuration);
        lock (_gate)
        {
            _current = snapshot;
        }

        return true;
    }
}
