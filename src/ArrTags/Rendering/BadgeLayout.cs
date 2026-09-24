using System;
using System.Collections.Generic;

namespace ArrTags.Rendering;

/// <summary>
/// The computed ADR-009/ADR-019 badge layout for one render surface: the
/// effective uniform scale, the priority-ordered technical rail pills placed per
/// the configured anchor, and the optional independent status pill. It contains
/// geometry and final visible text only, never source pixels or provider values
/// beyond the resolved selection.
/// </summary>
public sealed class BadgeLayout
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadgeLayout"/> class.
    /// </summary>
    /// <param name="scale">The uniform geometry scale.</param>
    /// <param name="technicalPills">The ordered technical rail pills.</param>
    /// <param name="statusPill">The optional status pill.</param>
    /// <exception cref="ArgumentNullException">The technical pill list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The scale is not positive.</exception>
    public BadgeLayout(
        double scale,
        IReadOnlyList<BadgePillPlacement> technicalPills,
        BadgePillPlacement? statusPill)
    {
        ArgumentNullException.ThrowIfNull(technicalPills);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        Scale = scale;
        TechnicalPills = technicalPills;
        StatusPill = statusPill;
    }

    /// <summary>
    /// Gets the effective uniform geometry scale, including the configured
    /// preset size factor and the safe-area clamp.
    /// </summary>
    public double Scale { get; }

    /// <summary>
    /// Gets the ordered technical rail pills. Their order is the ADR-009
    /// priority order and their <c>Row</c> is the packed rail row.
    /// </summary>
    public IReadOnlyList<BadgePillPlacement> TechnicalPills { get; }

    /// <summary>
    /// Gets the optional independent status pill. It is top-right unless the
    /// configured rail anchor is <see cref="BadgePosition.TopRight"/>, in which
    /// case it is top-left.
    /// </summary>
    public BadgePillPlacement? StatusPill { get; }

    /// <summary>
    /// Gets a value indicating whether the layout produced no drawable pill.
    /// </summary>
    public bool IsEmpty => TechnicalPills.Count == 0 && StatusPill is null;
}
