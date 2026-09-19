using System;
using System.Collections.Generic;
using ArrTags.Metadata;

namespace ArrTags.Rendering;

/// <summary>
/// Resolves the ordered provider-neutral <see cref="BadgeDefinition"/> snapshot
/// against canonical <see cref="BadgeMetadata"/>. It first applies the enabled
/// selectors through <see cref="BadgeSelectorResolver"/> so the ADR-009 semantic
/// priority is preserved, then applies each enabled definition's bounded template
/// to the confirmed value. Provider kind is never a rendering branch and unknown
/// values are still omitted.
/// </summary>
public static class BadgeDefinitionResolver
{
    /// <summary>
    /// Resolves the enabled definitions against one canonical metadata
    /// observation.
    /// </summary>
    /// <param name="metadata">The canonical metadata observation, or <see langword="null"/> when none exists.</param>
    /// <param name="definitions">The ordered definition snapshot; the first enabled definition for a selector wins.</param>
    /// <returns>The ordered, templated selection, or <see cref="BadgeSelection.Empty"/> when nothing is displayable.</returns>
    /// <exception cref="ArgumentNullException">The definitions are <see langword="null"/> or contain a <see langword="null"/> entry.</exception>
    public static BadgeSelection Resolve(BadgeMetadata? metadata, IReadOnlyList<BadgeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var bySelector = new Dictionary<BadgeSelector, BadgeDefinition>();
        var enabled = new HashSet<BadgeSelector>();
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);

            if (bySelector.ContainsKey(definition.Selector))
            {
                continue;
            }

            bySelector.Add(definition.Selector, definition);
            if (definition.Enabled)
            {
                enabled.Add(definition.Selector);
            }
        }

        var resolved = BadgeSelectorResolver.Resolve(metadata, enabled);
        if (resolved.IsEmpty)
        {
            return BadgeSelection.Empty;
        }

        var technicalValues = new List<BadgeValue>(resolved.TechnicalValues.Count);
        foreach (var value in resolved.TechnicalValues)
        {
            if (!bySelector.TryGetValue(value.Selector, out var definition) || !definition.Enabled)
            {
                continue;
            }

            technicalValues.Add(new BadgeValue(value.Selector, definition.ApplyTemplate(value.Text)));
        }

        BadgeValue? status = null;
        if (resolved.StatusValue is not null
            && bySelector.TryGetValue(BadgeSelector.UpgradePending, out var statusDefinition)
            && statusDefinition.Enabled)
        {
            status = new BadgeValue(
                BadgeSelector.UpgradePending,
                statusDefinition.ApplyTemplate(resolved.StatusValue.Text));
        }

        return technicalValues.Count == 0 && status is null
            ? BadgeSelection.Empty
            : new BadgeSelection(technicalValues, status);
    }
}
