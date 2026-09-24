using System;
using System.Collections.Generic;

namespace ArrTags.Rendering;

/// <summary>
/// The bounded, provider-neutral definition of one V1 badge. A definition names a
/// canonical <see cref="BadgeSelector"/>, may disable it, and carries one bounded
/// display template containing at most one <c>{value}</c> placeholder. It never
/// references a Sonarr or Radarr DTO path, record identifier, quality profile,
/// credential, or extension value, and it does not contain the current item's
/// metadata.
/// </summary>
/// <remarks>
/// Definition order cannot override the ADR-009 semantic priority. Configuration
/// controls visibility and bounded presentation only. The V1 default set is
/// code-owned until task 4.10 persists renderer configuration.
/// </remarks>
public sealed class BadgeDefinition
{
    /// <summary>
    /// The single provider-neutral value placeholder a template may contain.
    /// </summary>
    public const string ValuePlaceholder = "{value}";

    /// <summary>
    /// The maximum template length in characters. A template longer than this is
    /// rejected so an unbounded template cannot flow into rendering.
    /// </summary>
    public const int MaximumTemplateLength = 128;

    /// <summary>
    /// The maximum number of allowlist entries a definition may carry. A longer
    /// allowlist is rejected so an unbounded filter cannot flow into rendering.
    /// </summary>
    public const int MaximumAllowedValues = 32;

    /// <summary>
    /// The maximum allowlist entry length in characters. A longer entry is
    /// rejected so an unbounded filter cannot flow into rendering.
    /// </summary>
    public const int MaximumAllowedValueLength = 64;

    private static readonly IReadOnlyList<string> EmptyAllowedValues = Array.Empty<string>();

    private static readonly IReadOnlyList<BadgeDefinition> DefaultDefinitions = BuildDefaults();

    /// <summary>
    /// Initializes a new instance of the <see cref="BadgeDefinition"/> class.
    /// </summary>
    /// <param name="selector">The provider-neutral canonical field selector.</param>
    /// <param name="enabled">Whether the selector participates in rendering.</param>
    /// <param name="template">The bounded display template, for example <c>{value}</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The selector is not defined.</exception>
    /// <exception cref="ArgumentNullException">The template is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The template is empty, too long, or contains more than one placeholder.</exception>
    public BadgeDefinition(BadgeSelector selector, bool enabled, string template)
        : this(selector, enabled, template, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BadgeDefinition"/> class with
    /// a resolved value allowlist.
    /// </summary>
    /// <param name="selector">The provider-neutral canonical field selector.</param>
    /// <param name="enabled">Whether the selector participates in rendering.</param>
    /// <param name="template">The bounded display template, for example <c>{value}</c>.</param>
    /// <param name="allowedValues">
    /// The resolved allowlist, or <see langword="null"/> / an empty list for no
    /// restriction. Entries are trimmed and defensively copied.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The selector is not defined.</exception>
    /// <exception cref="ArgumentNullException">The template is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The template is unusable, or the allowlist exceeds the entry/length bound,
    /// contains a blank or control-character entry, or contains a duplicate after
    /// case-insensitive comparison.
    /// </exception>
    public BadgeDefinition(BadgeSelector selector, bool enabled, string template, IReadOnlyList<string>? allowedValues)
    {
        if (!Enum.IsDefined(selector))
        {
            throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown badge selector.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        if (template.Length > MaximumTemplateLength)
        {
            throw new ArgumentException(
                $"A badge template must not exceed {MaximumTemplateLength} characters.",
                nameof(template));
        }

        if (CountOccurrences(template, ValuePlaceholder) > 1)
        {
            throw new ArgumentException(
                "A badge template may contain at most one {value} placeholder.",
                nameof(template));
        }

        Selector = selector;
        Enabled = enabled;
        Template = template;
        AllowedValues = NormalizeAllowedValues(allowedValues);
    }

    /// <summary>
    /// Gets the code-owned V1 default definitions from ADR-009: every V1 selector
    /// is enabled, technical templates display the value with no field prefix,
    /// and the upgrade status template is the fixed text <c>UPGRADE</c>. The
    /// enumeration order is the definition order; semantic priority is applied by
    /// the selector resolver, not by this order.
    /// </summary>
    public static IReadOnlyList<BadgeDefinition> V1Default => DefaultDefinitions;

    /// <summary>
    /// Gets the provider-neutral canonical field selector.
    /// </summary>
    public BadgeSelector Selector { get; }

    /// <summary>
    /// Gets a value indicating whether the selector participates in rendering.
    /// A disabled definition produces no badge even when its value is confirmed.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Gets the bounded display template. It contains at most one
    /// <see cref="ValuePlaceholder"/>; a template without a placeholder renders
    /// its literal text, as the fixed upgrade-status template does.
    /// </summary>
    public string Template { get; }

    /// <summary>
    /// Gets the resolved, immutable, provider-neutral value allowlist
    /// (ADR-017). An empty list means no restriction. Entries are trimmed and
    /// preserve the configured order and case; matching is case-insensitive and
    /// order-independent.
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; }

    /// <summary>
    /// Applies the definition template to one confirmed canonical value. The
    /// result is still subject to the normalizer and layout text limits.
    /// </summary>
    /// <param name="value">The confirmed, non-empty normalized value text.</param>
    /// <returns>The templated display text.</returns>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public string ApplyTemplate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Template.Contains(ValuePlaceholder, StringComparison.Ordinal)
            ? Template.Replace(ValuePlaceholder, value, StringComparison.Ordinal)
            : Template;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static IReadOnlyList<string> NormalizeAllowedValues(IReadOnlyList<string>? allowedValues)
    {
        if (allowedValues is null || allowedValues.Count == 0)
        {
            return EmptyAllowedValues;
        }

        if (allowedValues.Count > MaximumAllowedValues)
        {
            throw new ArgumentException(
                $"A badge allowlist must not exceed {MaximumAllowedValues} entries.",
                nameof(allowedValues));
        }

        var normalized = new string[allowedValues.Count];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < allowedValues.Count; index++)
        {
            var value = allowedValues[index];
            if (value is null)
            {
                throw new ArgumentException(
                    "A badge allowlist entry must not be null.",
                    nameof(allowedValues));
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                throw new ArgumentException(
                    "A badge allowlist entry must not be blank.",
                    nameof(allowedValues));
            }

            if (trimmed.Length > MaximumAllowedValueLength)
            {
                throw new ArgumentException(
                    $"A badge allowlist entry must not exceed {MaximumAllowedValueLength} characters.",
                    nameof(allowedValues));
            }

            if (ContainsControlCharacter(trimmed))
            {
                throw new ArgumentException(
                    "A badge allowlist entry must not contain control characters.",
                    nameof(allowedValues));
            }

            if (!seen.Add(trimmed))
            {
                throw new ArgumentException(
                    "A badge allowlist entry must be unique (case-insensitive).",
                    nameof(allowedValues));
            }

            normalized[index] = trimmed;
        }

        return Array.AsReadOnly(normalized);
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

    private static IReadOnlyList<BadgeDefinition> BuildDefaults()
    {
        var definitions = new BadgeDefinition[]
        {
            new BadgeDefinition(BadgeSelector.Quality, true, ValuePlaceholder),
            new BadgeDefinition(BadgeSelector.Resolution, true, ValuePlaceholder),
            new BadgeDefinition(BadgeSelector.DynamicRange, true, ValuePlaceholder),
            new BadgeDefinition(BadgeSelector.Source, true, ValuePlaceholder),
            new BadgeDefinition(BadgeSelector.VideoCodec, true, ValuePlaceholder),
            new BadgeDefinition(BadgeSelector.Audio, true, ValuePlaceholder),
            new BadgeDefinition(BadgeSelector.CustomBadge, true, ValuePlaceholder),
            new BadgeDefinition(BadgeSelector.UpgradePending, true, BadgeSelectorResolver.UpgradeStatusText),
        };

        return Array.AsReadOnly(definitions);
    }
}
