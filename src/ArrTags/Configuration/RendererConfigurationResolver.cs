using System;
using System.Collections.Generic;
using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// Resolves persisted <see cref="RendererConfiguration"/> into the ordered
/// provider-neutral <see cref="BadgeDefinition"/> snapshot and the effective
/// <see cref="RenderOutputPolicy"/>. Missing selectors keep the code-owned
/// ADR-009 defaults, the definition order is the canonical selector order, and
/// every code-owned format, color-space, alpha, font, geometry, text, and version
/// value is preserved from <see cref="RenderOutputPolicy.Default"/>. Resolution
/// is tolerant so <see cref="PluginConfigurationSnapshot.From"/> remains usable
/// for a configuration that has not first passed validation.
/// </summary>
public static class RendererConfigurationResolver
{
    /// <summary>
    /// Resolves the ordered definition snapshot from a persisted renderer
    /// configuration.
    /// </summary>
    /// <param name="configuration">The persisted renderer configuration, or <see langword="null"/> for the code-owned defaults.</param>
    /// <returns>The ordered, immutable definition snapshot.</returns>
    public static IReadOnlyList<BadgeDefinition> ResolveDefinitions(RendererConfiguration? configuration)
    {
        var configured = new Dictionary<BadgeSelector, BadgeDefinition>();
        if (configuration is not null)
        {
            foreach (var entry in configuration.Selectors)
            {
                if (entry is null
                    || !Enum.IsDefined(entry.Selector)
                    || configured.ContainsKey(entry.Selector)
                    || !TryCreateDefinition(entry, out var definition))
                {
                    continue;
                }

                configured.Add(entry.Selector, definition);
            }
        }

        var defaults = BadgeDefinition.V1Default;
        var resolved = new List<BadgeDefinition>(defaults.Count);
        foreach (var defaultDefinition in defaults)
        {
            resolved.Add(configured.TryGetValue(defaultDefinition.Selector, out var configuredDefinition)
                ? configuredDefinition
                : defaultDefinition);
        }

        return resolved.AsReadOnly();
    }

    /// <summary>
    /// Resolves the effective output policy from a persisted renderer
    /// configuration. The configured palette override is applied to the four
    /// palette colors; every other value remains code-owned.
    /// </summary>
    /// <param name="configuration">The persisted renderer configuration, or <see langword="null"/> for the code-owned defaults.</param>
    /// <returns>The effective immutable output policy.</returns>
    public static RenderOutputPolicy ResolveOutputPolicy(RendererConfiguration? configuration)
    {
        var defaults = RenderOutputPolicy.Default;
        return new RenderOutputPolicy
        {
            OutputFormat = defaults.OutputFormat,
            ColorSpace = defaults.ColorSpace,
            AlphaPolicy = defaults.AlphaPolicy,
            FontIdentity = defaults.FontIdentity,
            TechnicalBackground = RendererPalette.Resolve(configuration?.TechnicalBackground, defaults.TechnicalBackground),
            TechnicalText = RendererPalette.Resolve(configuration?.TechnicalText, defaults.TechnicalText),
            StatusBackground = RendererPalette.Resolve(configuration?.StatusBackground, defaults.StatusBackground),
            StatusText = RendererPalette.Resolve(configuration?.StatusText, defaults.StatusText),
            ScaleReferenceWidth = defaults.ScaleReferenceWidth,
            MinimumScale = defaults.MinimumScale,
            MaximumScale = defaults.MaximumScale,
            MaximumScalarValues = defaults.MaximumScalarValues,
            RetainedPrefixScalarValues = defaults.RetainedPrefixScalarValues,
            Ellipsis = defaults.Ellipsis,
        };
    }

    /// <summary>
    /// Computes the secret-free renderer configuration fingerprint for an already
    /// resolved definition snapshot and effective output policy.
    /// </summary>
    /// <param name="definitions">The ordered resolved badge definitions.</param>
    /// <param name="outputPolicy">The effective output policy.</param>
    /// <returns>The non-empty uppercase SHA-256 configuration fingerprint.</returns>
    public static string ComputeFingerprint(IReadOnlyList<BadgeDefinition> definitions, RenderOutputPolicy outputPolicy)
    {
        return RendererConfigurationFingerprint.Compute(definitions, outputPolicy);
    }

    private static bool TryCreateDefinition(BadgeSelectorConfiguration entry, out BadgeDefinition definition)
    {
        try
        {
            definition = new BadgeDefinition(entry.Selector, entry.Enabled, entry.Template);
            return true;
        }
        catch (ArgumentException)
        {
            // ArgumentOutOfRangeException also derives from ArgumentException; a
            // candidate that has not passed validation simply falls back to the
            // code-owned default for that selector.
            definition = null!;
            return false;
        }
    }
}
