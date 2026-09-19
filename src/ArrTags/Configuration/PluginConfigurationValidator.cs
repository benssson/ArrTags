using System;
using System.Collections.Generic;

namespace ArrTags.Configuration;

/// <summary>
/// Validates a candidate <see cref="PluginConfiguration"/> before it becomes an
/// active snapshot. Validation messages never contain secret values.
/// </summary>
public static class PluginConfigurationValidator
{
    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 120;

    /// <summary>
    /// Validates the supplied configuration.
    /// </summary>
    /// <param name="configuration">The candidate configuration.</param>
    /// <returns>A safe validation result.</returns>
    public static ConfigurationValidationResult Validate(PluginConfiguration? configuration)
    {
        var errors = new List<string>();

        if (configuration is null)
        {
            errors.Add("Configuration is null.");
            return new ConfigurationValidationResult(errors);
        }

        ValidateConnection("Sonarr", configuration.Sonarr, errors);
        ValidateConnection("Radarr", configuration.Radarr, errors);
        ValidateLibraryScope(configuration.EnabledLibraries, errors);
        (configuration.Limits ?? new OperationalLimits()).Validate(errors);
        (configuration.Renderer ?? new RendererConfiguration()).Validate(errors);

        return new ConfigurationValidationResult(errors);
    }

    private static void ValidateConnection(string name, ArrConnectionConfiguration? connection, List<string> errors)
    {
        if (connection is null)
        {
            errors.Add(FormattableString.Invariant($"{name} connection configuration is missing."));
            return;
        }

        if (connection.RequestTimeoutSeconds < MinTimeoutSeconds || connection.RequestTimeoutSeconds > MaxTimeoutSeconds)
        {
            errors.Add(FormattableString.Invariant(
                $"{name} request timeout must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds} seconds."));
        }

        if (!connection.Enabled)
        {
            return;
        }

        if (!IsAbsoluteHttpUrl(connection.BaseUrl))
        {
            errors.Add(FormattableString.Invariant($"{name} base URL must be an absolute http or https URL."));
        }

        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            errors.Add(FormattableString.Invariant($"{name} API key is required when the connection is enabled."));
        }
    }

    private static bool IsAbsoluteHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
    }

    private static void ValidateLibraryScope(IEnumerable<string> libraries, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var library in libraries)
        {
            if (string.IsNullOrWhiteSpace(library))
            {
                errors.Add("Library scope entries must not be empty.");
                continue;
            }

            if (!seen.Add(library))
            {
                errors.Add("Library scope entries must be unique.");
            }
        }
    }
}
