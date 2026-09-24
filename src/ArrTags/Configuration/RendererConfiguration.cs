using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// Persisted, user-adjustable V1 renderer configuration. It contains only the
/// enabled/disabled badge selectors, their bounded provider-neutral templates,
/// their optional bounded value allowlists, the global badge position and size,
/// plus optional contrast-validated palette overrides. Output format, color
/// space, alpha policy, font identity, reference geometry, text limits, and the
/// renderer version remain code-owned per ADR-010 and are not represented here.
/// </summary>
/// <remarks>
/// A selector that is absent keeps the code-owned ADR-009 default. A template
/// contains at most one <c>{value}</c> placeholder; it is a literal or
/// placeholder substitution over the already provider-neutral resolved value and
/// therefore cannot reference a provider DTO path, record identifier, quality
/// profile, credential, or extension. An empty allowlist means no restriction.
/// An empty palette value keeps the ADR-009 default color.
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

    private Collection<BadgeSelectorConfiguration> _selectors = new Collection<BadgeSelectorConfiguration>();

    /// <summary>
    /// Gets or sets the configured selector entries. Entries must name a known V1
    /// selector, must be unique, and must carry a bounded valid template. A null
    /// value is treated as an empty set.
    /// </summary>
    /// <remarks>
    /// The property is settable so the elevation-gated <c>PluginsController</c>
    /// POST round-trip can populate it. The pinned Jellyfin 12.0.0
    /// deserialization options do not populate a get-only collection property
    /// and would silently drop the value (ADR-016 clause 7).
    /// </remarks>
#pragma warning disable CA2227 // The configuration is a replacement-snapshot DTO; the setter is required for the POST round-trip.
    public Collection<BadgeSelectorConfiguration> Selectors
    {
        get => _selectors;
        set => _selectors = value ?? new Collection<BadgeSelectorConfiguration>();
    }
#pragma warning restore CA2227

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
    /// Gets or sets the global technical-rail anchor (ADR-019). The default
    /// <see cref="BadgePosition.BottomLeft"/> reproduces the V1 output.
    /// Placement is global renderer policy; it is not representable per
    /// selector.
    /// </summary>
    public BadgePosition Position { get; set; } = BadgePosition.BottomLeft;

    /// <summary>
    /// Gets or sets the global preset badge size (ADR-019). The default
    /// <see cref="BadgeSize.Medium"/> reproduces the V1 geometry. Size is global
    /// renderer policy; it is not representable per selector.
    /// </summary>
    public BadgeSize Size { get; set; } = BadgeSize.Medium;

    /// <summary>
    /// Validates the configured selectors, templates, allowlists, position,
    /// size, colors, and contrast, and appends a safe message for each violation.
    /// Messages never contain a secret, a template value, an allowlist value, or
    /// a configured color value.
    /// </summary>
    /// <param name="errors">The bounded error collection to append to.</param>
    public void Validate(ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (!Enum.IsDefined(Position))
        {
            errors.Add("Renderer badge position must be a known value.");
        }

        if (!Enum.IsDefined(Size))
        {
            errors.Add("Renderer badge size must be a known value.");
        }

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
            ValidateAllowedValues(entry.Selector, entry.AllowedValues, errors);
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

    private static void ValidateAllowedValues(BadgeSelector selector, IReadOnlyList<string>? allowedValues, ICollection<string> errors)
    {
        if (allowedValues is null || allowedValues.Count == 0)
        {
            return;
        }

        if (allowedValues.Count > BadgeDefinition.MaximumAllowedValues)
        {
            errors.Add(FormattableString.Invariant(
                $"Renderer allowlist for selector '{selector}' must not exceed {BadgeDefinition.MaximumAllowedValues} entries."));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in allowedValues)
        {
            if (value is null)
            {
                errors.Add(FormattableString.Invariant(
                    $"Renderer allowlist entries for selector '{selector}' must not be null."));
                continue;
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                errors.Add(FormattableString.Invariant(
                    $"Renderer allowlist entries for selector '{selector}' must not be blank."));
                continue;
            }

            if (trimmed.Length > BadgeDefinition.MaximumAllowedValueLength)
            {
                errors.Add(FormattableString.Invariant(
                    $"Renderer allowlist entry for selector '{selector}' must not exceed {BadgeDefinition.MaximumAllowedValueLength} characters."));
            }

            if (ContainsControlCharacter(trimmed))
            {
                errors.Add(FormattableString.Invariant(
                    $"Renderer allowlist entries for selector '{selector}' must not contain control characters."));
            }

            if (!seen.Add(trimmed))
            {
                errors.Add(FormattableString.Invariant(
                    $"Renderer allowlist entries for selector '{selector}' must be unique (case-insensitive)."));
            }
        }
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
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
