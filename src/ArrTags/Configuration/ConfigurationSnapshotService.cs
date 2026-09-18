using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ArrTags.Secrets;

namespace ArrTags.Configuration;

/// <summary>
/// Owns the active configuration snapshot and its private, version-matched
/// secret map. A replacement is activated only when the candidate validates;
/// an invalid replacement leaves both the public snapshot and the private
/// secrets unchanged. The service is also the credential boundary: it issues
/// short-lived leases and never exposes secret values to canonical state.
/// </summary>
public sealed class ConfigurationSnapshotService : IPluginSecretResolver
{
    private const long InitialConfigurationVersion = 1;

    private readonly object _gate = new object();
    private PluginConfigurationSnapshot _current;
    private IReadOnlyDictionary<SecretReference, string> _secrets;
    private long _configurationVersion;

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
        var effective = result.IsValid ? configuration : new PluginConfiguration();

        _configurationVersion = InitialConfigurationVersion;
        _current = PluginConfigurationSnapshot.From(effective, _configurationVersion);
        _secrets = BuildSecrets(effective);
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
    /// The previous snapshot and secrets remain active when validation fails.
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

        lock (_gate)
        {
            var version = _configurationVersion + 1;
            _configurationVersion = version;
            _current = PluginConfigurationSnapshot.From(configuration, version);
            _secrets = BuildSecrets(configuration);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryAcquire(SecretReference reference, long configurationVersion, [NotNullWhen(true)] out SecretLease? lease)
    {
        ArgumentNullException.ThrowIfNull(reference);

        lock (_gate)
        {
            if (configurationVersion != _configurationVersion
                || !_secrets.TryGetValue(reference, out var value))
            {
                lease = null;
                return false;
            }

            lease = new SecretLease(reference, value);
            return true;
        }
    }

    private static IReadOnlyDictionary<SecretReference, string> BuildSecrets(PluginConfiguration configuration)
    {
        var secrets = new Dictionary<SecretReference, string>(3);
        AddSecret(secrets, SecretReference.SonarrApiKey, configuration.Sonarr?.ApiKey);
        AddSecret(secrets, SecretReference.RadarrApiKey, configuration.Radarr?.ApiKey);
        AddSecret(secrets, SecretReference.WebhookAuthentication, configuration.WebhookSecret);
        return secrets;
    }

    private static void AddSecret(Dictionary<SecretReference, string> secrets, SecretReference reference, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            secrets[reference] = value;
        }
    }
}
