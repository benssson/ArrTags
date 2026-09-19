using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// Persisted, user-adjustable V1 renderer configuration. It contains only the
/// enabled/disabled badge selectors and their bounded provider-neutral templates,
/// plus optional contrast-validated palette overrides. Output format, color
/// space, alpha policy, font identity, geometry, text limits, and the renderer
/// version remain code-owned per ADR-010 and are not represented here.
/// </summary>
/// <remarks>
/// A selector that is absent keeps the code-owned ADR-009 default. A template
/// contains at most one <c>{value}</c> placeholder; it is a literal or
/// placeholder substitution over the already provider-neutral resolved value and
/// therefore cannot reference a provider DTO path, record identifier, quality
/// profile, credential, or extension. An empty palette value keeps the ADR-009
/// default color.
/// </remarks>
public sealed class RendererConfiguration
{
    /// <summary>
    /// The code-owned renderer configuration schema version from ADR-010. It is
    /// not user-selectable and changes only when the meaning or shape of the
    /// persisted renderer configuration changes; it participates in the renderer
    /// configuration fingerprint.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets the configured selector entries. Entries must name a known V1
    /// selector, must be unique, and must carry a bounded valid template.
    /// </summary>
    public Collection<BadgeSelectorConfiguration> Selectors { get; } = new Collection<BadgeSelectorConfiguration>();

    /// <summary>
    /// Gets or sets the technical-badge background override. An empty value keeps
    /// the ADR-009 default.
    /// </summary>
    public string TechnicalBackground { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the technical-badge text override. An empty value keeps the
    /// ADR-009 default.
    /// </summary>
    public string TechnicalText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the upgrade-status background override. An empty value keeps
    /// the ADR-009 default.
    /// </summary>
    public string StatusBackground { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the upgrade-status text override. An empty value keeps the
    /// ADR-009 default.
    /// </summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// Validates the configured selectors, templates, colors, and contrast, and
    /// appends a safe message for each violation. Messages never contain a
    /// secret, a template value, or a configured color value.
    /// </summary>
    /// <param name="errors">The bounded error collection to append to.</param>
    public void Validate(ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var seen = new HashSet<BadgeSelector>();
        foreach (var entry in Selectors)
        {
            if (entry is null)
            {
                errors.Add("Renderer selector entries must not be null.");
                continue;
            }

            if (!Enum.IsDefined(entry.Selector))
            {
                errors.Add("Renderer selector entries must name a known V1 selector.");
                continue;
            }

            if (!seen.Add(entry.Selector))
            {
                errors.Add("Renderer selector entries must be unique.");
                continue;
            }

            ValidateTemplate(entry.Selector, entry.Template, errors);
        }

        ValidatePalette(errors);
    }

    private static void ValidateTemplate(BadgeSelector selector, string? template, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add(FormattableString.Invariant(
                $"Renderer template for selector '{selector}' must not be empty."));
            return;
        }

        if (template.Length > BadgeDefinition.MaximumTemplateLength)
        {
            errors.Add(FormattableString.Invariant(
                $"Renderer template for selector '{selector}' must not exceed {BadgeDefinition.MaximumTemplateLength} characters."));
        }

        if (CountValuePlaceholders(template) > 1)
        {
            errors.Add(FormattableString.Invariant(
                $"Renderer template for selector '{selector}' may contain at most one {BadgeDefinition.ValuePlaceholder} placeholder."));
        }
    }

    private static int CountValuePlaceholders(string template)
    {
        var count = 0;
        var index = 0;
        while ((index = template.IndexOf(BadgeDefinition.ValuePlaceholder, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += BadgeDefinition.ValuePlaceholder.Length;
        }

        return count;
    }

    private void ValidatePalette(ICollection<string> errors)
    {
        var defaults = RenderOutputPolicy.Default;
        ValidateStyle(
            "technical",
            TechnicalText,
            defaults.TechnicalText,
            TechnicalBackground,
            defaults.TechnicalBackground,
            errors);
        ValidateStyle(
            "upgrade status",
            StatusText,
            defaults.StatusText,
            StatusBackground,
            defaults.StatusBackground,
            errors);
    }

    private static void ValidateStyle(
        string name,
        string? configuredText,
        string defaultText,
        string? configuredBackground,
        string defaultBackground,
        ICollection<string> errors)
    {
        var textValid = TryValidateColor(name, "text", configuredText, defaultText, errors, out var text);
        var backgroundValid = TryValidateColor(name, "background", configuredBackground, defaultBackground, errors, out var background);

        if (textValid && backgroundValid && RgbColor.ContrastRatio(text, background) < BadgeContrast.MinimumRatio)
        {
            errors.Add(FormattableString.Invariant(
                $"Renderer {name} text/background contrast must be at least {BadgeContrast.MinimumRatio:0.0}:1."));
        }
    }

    private static bool TryValidateColor(
        string name,
        string role,
        string? configured,
        string defaultValue,
        ICollection<string> errors,
        out RgbColor color)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!RgbColor.TryParse(defaultValue, out color))
            {
                color = default;
            }

            return true;
        }

        if (!RgbColor.TryParse(configured, out color))
        {
            errors.Add(FormattableString.Invariant(
                $"Renderer {name} {role} color must be an RRGGBB hexadecimal color value."));
            return false;
        }

        return true;
    }
}
