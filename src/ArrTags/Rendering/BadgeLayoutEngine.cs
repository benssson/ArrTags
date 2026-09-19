using System;
using System.Collections.Generic;

namespace ArrTags.Rendering;

/// <summary>
/// The pure ADR-009 rail layout engine. It packs the ordered technical values
/// into at most two bottom-left rows of at most three single-line rounded pills,
/// shortens a label that cannot fit a full rail row, omits a label that cannot
/// produce a visible value, and places the independent status pill at the
/// top-right. It measures text through a caller-supplied delegate, so the layout
/// math is testable without a raster library.
/// </summary>
public static class BadgeLayoutEngine
{
    /// <summary>
    /// Builds the badge layout for one render surface.
    /// </summary>
    /// <param name="technicalValues">The ordered technical values in ADR-009 priority order.</param>
    /// <param name="statusValue">The optional upgrade-status value.</param>
    /// <param name="outputWidth">The oriented output width in pixels.</param>
    /// <param name="outputHeight">The oriented output height in pixels.</param>
    /// <param name="policy">The effective output policy.</param>
    /// <param name="measureText">Measures one label's width in output pixels.</param>
    /// <returns>The computed layout. It is empty when nothing can be drawn.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An output dimension is not positive.</exception>
    public static BadgeLayout Build(
        IReadOnlyList<BadgeValue> technicalValues,
        BadgeValue? statusValue,
        int outputWidth,
        int outputHeight,
        RenderOutputPolicy policy,
        Func<string, float> measureText)
    {
        ArgumentNullException.ThrowIfNull(technicalValues);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(measureText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputHeight);

        var scale = BadgeGeometry.ComputeScale(outputWidth, policy);
        var inset = BadgeGeometry.OuterInset * scale;
        var pillGap = BadgeGeometry.PillGap * scale;
        var rowGap = BadgeGeometry.RowGap * scale;
        var pillHeight = BadgeGeometry.PillHeight * scale;
        var horizontalPadding = BadgeGeometry.HorizontalPadding * scale;
        var safeWidth = outputWidth - (2 * inset);

        var safeHeight = outputHeight - (2 * inset);
        var statusPill = BuildStatusPill(
            statusValue,
            policy,
            measureText,
            outputWidth,
            inset,
            pillHeight,
            horizontalPadding,
            safeWidth,
            safeHeight);

        var availableRows = ComputeAvailableRows(outputHeight, inset, pillHeight, rowGap);
        if (statusPill is not null)
        {
            var statusReserve = inset + pillHeight + rowGap;
            while (availableRows > 0 && TopOfRow(availableRows - 1, outputHeight, inset, pillHeight, rowGap) < statusReserve)
            {
                availableRows--;
            }
        }

        var placements = new List<BadgePillPlacement>(technicalValues.Count);
        if (safeWidth > 0 && availableRows > 0)
        {
            PackTechnicalValues(
                technicalValues,
                policy,
                measureText,
                outputHeight,
                inset,
                pillGap,
                rowGap,
                pillHeight,
                horizontalPadding,
                safeWidth,
                availableRows,
                placements);
        }

        return new BadgeLayout(scale, placements, statusPill);
    }

    private static int ComputeAvailableRows(int outputHeight, double inset, double pillHeight, double rowGap)
    {
        var rows = 0;
        while (rows < BadgeGeometry.MaximumRows)
        {
            var top = TopOfRow(rows, outputHeight, inset, pillHeight, rowGap);
            if (top < inset)
            {
                break;
            }

            rows++;
        }

        return rows;
    }

    private static double TopOfRow(int row, int outputHeight, double inset, double pillHeight, double rowGap)
    {
        return outputHeight - inset - ((row + 1) * pillHeight) - (row * rowGap);
    }

    private static void PackTechnicalValues(
        IReadOnlyList<BadgeValue> technicalValues,
        RenderOutputPolicy policy,
        Func<string, float> measureText,
        int outputHeight,
        double inset,
        double pillGap,
        double rowGap,
        double pillHeight,
        double horizontalPadding,
        double safeWidth,
        int availableRows,
        List<BadgePillPlacement> placements)
    {
        var rowWidths = new double[availableRows];
        var rowCounts = new int[availableRows];
        var row = 0;
        var maximumTextWidth = safeWidth - (2 * horizontalPadding);

        foreach (var value in technicalValues)
        {
            var normalized = BadgeTextNormalizer.Normalize(value.Text, policy);
            if (normalized.IsOmitted)
            {
                continue;
            }

            var fitted = FitToWidth(normalized.Text!, maximumTextWidth, measureText, policy);
            if (fitted is null)
            {
                continue;
            }

            var pillWidth = measureText(fitted) + (2 * horizontalPadding);
            if (pillWidth > safeWidth)
            {
                continue;
            }

            while (row < availableRows
                && !Fits(row, pillWidth, rowCounts, rowWidths, pillGap, safeWidth))
            {
                row++;
            }

            if (row >= availableRows)
            {
                break;
            }

            var x = rowCounts[row] == 0
                ? inset
                : inset + rowWidths[row] + pillGap;
            var y = TopOfRow(row, outputHeight, inset, pillHeight, rowGap);

            placements.Add(new BadgePillPlacement(value.Selector, fitted, false, row, x, y, pillWidth, pillHeight));

            rowWidths[row] += (rowCounts[row] == 0 ? 0 : pillGap) + pillWidth;
            rowCounts[row]++;
        }
    }

    private static BadgePillPlacement? BuildStatusPill(
        BadgeValue? statusValue,
        RenderOutputPolicy policy,
        Func<string, float> measureText,
        int outputWidth,
        double inset,
        double pillHeight,
        double horizontalPadding,
        double safeWidth,
        double safeHeight)
    {
        if (statusValue is null || safeWidth <= 0 || safeHeight < pillHeight)
        {
            return null;
        }

        var normalized = BadgeTextNormalizer.Normalize(statusValue.Text, policy);
        if (normalized.IsOmitted)
        {
            return null;
        }

        var fitted = FitToWidth(
            normalized.Text!,
            safeWidth - (2 * horizontalPadding),
            measureText,
            policy);
        if (fitted is null)
        {
            return null;
        }

        var pillWidth = measureText(fitted) + (2 * horizontalPadding);
        if (pillWidth > safeWidth)
        {
            return null;
        }

        var x = outputWidth - inset - pillWidth;
        return new BadgePillPlacement(statusValue.Selector, fitted, true, -1, x, inset, pillWidth, pillHeight);
    }

    private static bool Fits(
        int row,
        double pillWidth,
        int[] rowCounts,
        double[] rowWidths,
        double pillGap,
        double safeWidth)
    {
        if (rowCounts[row] >= BadgeGeometry.MaximumPillsPerRow)
        {
            return false;
        }

        var needed = rowCounts[row] == 0
            ? pillWidth
            : rowWidths[row] + pillGap + pillWidth;
        return needed <= safeWidth;
    }

    private static string? FitToWidth(
        string text,
        double maximumWidth,
        Func<string, float> measureText,
        RenderOutputPolicy policy)
    {
        if (measureText(text) <= maximumWidth)
        {
            return text;
        }

        // The final visible text, including the truncation marker, must stay
        // within the ADR-009 scalar limit. The input text is already normalized
        // to that limit, so the shortened prefix plus the marker must not grow
        // past it.
        var maximumRetained = policy.MaximumScalarValues - CountScalarValues(policy.Ellipsis);
        if (maximumRetained < 1)
        {
            return null;
        }

        var scalarCount = CountScalarValues(text);
        var upperBound = Math.Min(scalarCount - 1, maximumRetained);
        string? best = null;
        for (var retained = 1; retained <= upperBound; retained++)
        {
            var candidate = BadgeTextNormalizer.Shorten(text, retained, policy.Ellipsis);
            if (measureText(candidate) > maximumWidth)
            {
                break;
            }

            best = candidate;
        }

        return best;
    }

    private static int CountScalarValues(string value)
    {
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            _ = rune;
            count++;
        }

        return count;
    }
}
