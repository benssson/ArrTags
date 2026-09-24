using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// Computes the secret-free, deterministic renderer configuration fingerprint.
/// It covers every output-affecting renderer-configuration value: the ordered
/// selector enablement, bounded templates, and non-empty resolved allowlists, and
/// the effective technical and status palette. It deliberately excludes
/// credentials, the webhook secret, timestamps, correlation identifiers, and
/// every code-owned output value that <see cref="RenderFingerprint"/> already
/// covers. Credentials and the webhook secret are absent by construction, so
/// rotating a secret cannot change the fingerprint.
/// </summary>
public static class RendererConfigurationFingerprint
{
    /// <summary>
    /// Computes the uppercase SHA-256 renderer configuration fingerprint over the
    /// ordered definitions (selector, enablement, template, and any non-empty
    /// allowlist) and the effective palette.
    /// </summary>
    /// <param name="definitions">The ordered resolved badge definitions.</param>
    /// <param name="outputPolicy">The effective output policy.</param>
    /// <returns>The non-empty uppercase SHA-256 configuration fingerprint.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static string Compute(IReadOnlyList<BadgeDefinition> definitions, RenderOutputPolicy outputPolicy)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(outputPolicy);

        var builder = new StringBuilder();
        Append(builder, "rendererConfigurationSchemaVersion", RendererConfiguration.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, "definitionCount", definitions.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var definition in definitions)
        {
            Append(builder, "selector", definition.Selector.ToString());
            Append(builder, "selectorEnabled", definition.Enabled ? "true" : "false");
            Append(builder, "selectorTemplate", definition.Template);
            AppendAllowedValues(builder, definition.AllowedValues);
        }

        Append(builder, "paletteTechnicalBackground", outputPolicy.TechnicalBackground);
        Append(builder, "paletteTechnicalText", outputPolicy.TechnicalText);
        Append(builder, "paletteStatusBackground", outputPolicy.StatusBackground);
        Append(builder, "paletteStatusText", outputPolicy.StatusText);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        builder.Append(name)
            .Append('=')
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');
    }

    private static void AppendAllowedValues(StringBuilder builder, IReadOnlyList<string> allowedValues)
    {
        if (allowedValues.Count == 0)
        {
            // An empty allowlist means no restriction, so it is identity-neutral:
            // it contributes nothing and the default configuration's fingerprint
            // is unchanged. Only a non-empty allowlist changes the fingerprint.
            return;
        }

        Append(builder, "selectorAllowedValueCount", allowedValues.Count.ToString(CultureInfo.InvariantCulture));

        // Matching is case-insensitive and order-independent, so normalize case
        // and entry order before hashing: two configurations that differ only by
        // allowlist case or entry order have the same fingerprint.
        var normalized = new string[allowedValues.Count];
        for (var index = 0; index < allowedValues.Count; index++)
        {
            normalized[index] = allowedValues[index].ToUpperInvariant();
        }

        Array.Sort(normalized, StringComparer.Ordinal);
        foreach (var value in normalized)
        {
            Append(builder, "selectorAllowedValue", value);
        }
    }
}
