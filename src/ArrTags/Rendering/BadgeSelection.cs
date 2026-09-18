using System;
using System.Collections.Generic;
using System.Linq;

namespace ArrTags.Rendering;

/// <summary>
/// The ordered, provider-neutral result of resolving ADR-009 selectors against
/// one canonical metadata observation. Technical values are already in the
/// code-owned priority order; lower-priority layout and text policy are applied
/// later by the renderer and are not part of selector resolution.
/// </summary>
public sealed class BadgeSelection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadgeSelection"/> class.
    /// </summary>
    /// <param name="technicalValues">The ordered technical values.</param>
    /// <param name="statusValue">The optional upgrade-status value.</param>
    /// <exception cref="ArgumentNullException">The technical values are <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A value has the wrong kind for its position.</exception>
    public BadgeSelection(IReadOnlyList<BadgeValue> technicalValues, BadgeValue? statusValue)
    {
        ArgumentNullException.ThrowIfNull(technicalValues);

        if (statusValue is not null && statusValue.Kind != BadgeValueKind.Status)
        {
            throw new ArgumentException("The status value must be an upgrade-status value.", nameof(statusValue));
        }

        foreach (var value in technicalValues)
        {
            if (value.Kind != BadgeValueKind.Technical)
            {
                throw new ArgumentException(
                    "Technical values must not contain a status value.",
                    nameof(technicalValues));
            }
        }

        TechnicalValues = technicalValues.ToArray();
        StatusValue = statusValue;
    }

    /// <summary>
    /// Gets the empty selection shared by every metadata observation that has no
    /// displayed value.
    /// </summary>
    public static BadgeSelection Empty { get; } = new BadgeSelection(Array.Empty<BadgeValue>(), null);

    /// <summary>
    /// Gets the ordered technical values. The order is the ADR-009 priority
    /// order, not the configuration definition order.
    /// </summary>
    public IReadOnlyList<BadgeValue> TechnicalValues { get; }

    /// <summary>
    /// Gets the optional upgrade-status value.
    /// </summary>
    public BadgeValue? StatusValue { get; }

    /// <summary>
    /// Gets a value indicating whether no value was resolved.
    /// </summary>
    public bool IsEmpty => TechnicalValues.Count == 0 && StatusValue is null;
}
