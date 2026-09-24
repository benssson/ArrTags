using System;
using System.Collections.Generic;

namespace ArrTags.Rendering;

/// <summary>
/// The pure ADR-009/ADR-019 rail layout engine. It packs the ordered technical
/// values into at most two rows of at most three single-line rounded pills,
/// shortens a label that cannot fit a full rail row, omits a label that cannot
/// produce a visible value, and places the independent status pill. The rail is
/// anchored per the policy's <see cref="BadgePosition"/>: rows stack away from
/// the anchored edge (downward for top anchors, upward for bottom anchors, and
/// vertically centered for <see cref="BadgePosition.Center"/>) and align to the
/// anchored side (left for left anchors, right for right anchors, centered for
/// <see cref="BadgePosition.Center"/>). The status pill is top-right except when
/// the rail anchor is <see cref="BadgePosition.TopRight"/>, then top-left. The
/// engine measures text through a caller-supplied delegate, so the layout math
/// is testable without a raster library.
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
    /// <param name="policy">The effective output policy, including the position and size.</param>
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

        var position = policy.Position;
        var scale = BadgeGeometry.ComputeEffectiveScale(outputWidth, outputHeight, policy);
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
            safeHeight,
            position);

        var availableRows = ComputeAvailableRows(safeHeight, pillHeight, rowGap);
        var rowAvailableWidth = new double[BadgeGeometry.MaximumRows];
        for (var row = 0; row < rowAvailableWidth.Length; row++)
        {
            rowAvailableWidth[row] = safeWidth;
        }

        if (statusPill is not null)
        {
            var statusReserve = inset + pillHeight + rowGap;
            if (IsTopAnchor(position))
            {
                // The rail's first row shares the top band with the status pill,
                // which is placed on the opposite side, so reserve the status
                // pill plus one gap from that row only. Later rows sit below the
                // status band and keep the full safe width.
                rowAvailableWidth[0] = safeWidth - statusPill.Width - pillGap;
            }
            else if (position == BadgePosition.Center)
            {
                // Keep the vertically centered block below the top status band.
                while (availableRows > 0
                    && CenteredBlockTop(availableRows, outputHeight, pillHeight, rowGap) < statusReserve)
                {
                    availableRows--;
                }
            }
            else
            {
                // Bottom anchors: the rail stacks upward, so keep its topmost
                // row below the top status band.
                while (availableRows > 0
                    && BottomRowTop(availableRows - 1, outputHeight, inset, pillHeight, rowGap) < statusReserve)
                {
                    availableRows--;
                }
            }
        }

        var placements = new List<BadgePillPlacement>(technicalValues.Count);
        if (safeWidth > 0 && availableRows > 0)
        {
            PackTechnicalValues(
                technicalValues,
                policy,
                measureText,
                outputWidth,
                outputHeight,
                inset,
                pillGap,
                rowGap,
                pillHeight,
                horizontalPadding,
                safeWidth,
                availableRows,
                rowAvailableWidth,
                position,
                placements);
        }

        return new BadgeLayout(scale, placements, statusPill);
    }

    private static bool IsTopAnchor(BadgePosition position)
    {
        return position is BadgePosition.TopLeft or BadgePosition.TopRight;
    }

    private static int ComputeAvailableRows(double safeHeight, double pillHeight, double rowGap)
    {
        var rows = 0;
        while (rows < BadgeGeometry.MaximumRows)
        {
            var blockHeight = ((rows + 1) * pillHeight) + (rows * rowGap);
            if (blockHeight > safeHeight)
            {
                break;
            }

            rows++;
        }

        return rows;
    }

    private static double BottomRowTop(int row, int outputHeight, double inset, double pillHeight, double rowGap)
    {
        return outputHeight - inset - pillHeight - (row * (pillHeight + rowGap));
    }

    private static double CenteredBlockTop(int rowCount, int outputHeight, double pillHeight, double rowGap)
    {
        if (rowCount <= 0)
        {
            return outputHeight / 2.0;
        }

        var blockHeight = (rowCount * pillHeight) + ((rowCount - 1) * rowGap);
        return (outputHeight - blockHeight) / 2.0;
    }

    private static void PackTechnicalValues(
        IReadOnlyList<BadgeValue> technicalValues,
        RenderOutputPolicy policy,
        Func<string, float> measureText,
        int outputWidth,
        int outputHeight,
        double inset,
        double pillGap,
        double rowGap,
        double pillHeight,
        double horizontalPadding,
        double safeWidth,
        int availableRows,
        double[] rowAvailableWidth,
        BadgePosition position,
        List<BadgePillPlacement> placements)
    {
        var rows = new List<PackedPill>[availableRows];
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
                && !Fits(row, pillWidth, rowCounts, rowWidths, pillGap, rowAvailableWidth))
            {
                row++;
            }

            if (row >= availableRows)
            {
                break;
            }

            var packed = new PackedPill(value.Selector, fitted, pillWidth);
            (rows[row] ??= new List<PackedPill>()).Add(packed);
            rowWidths[row] += (rowCounts[row] == 0 ? 0 : pillGap) + pillWidth;
            rowCounts[row]++;
        }

        var usedRows = 0;
        for (var index = 0; index < availableRows; index++)
        {
            if (rowCounts[index] > 0)
            {
                usedRows = index + 1;
            }
        }

        if (usedRows == 0)
        {
            return;
        }

        var rowTops = ComputeRowTops(usedRows, outputHeight, inset, pillHeight, rowGap, position);
        for (var index = 0; index < usedRows; index++)
        {
            if (rows[index] is not List<PackedPill> packedRow || packedRow.Count == 0)
            {
                continue;
            }

            var rowTotal = rowWidths[index];
            var x = position switch
            {
                BadgePosition.TopRight or BadgePosition.BottomRight => outputWidth - inset - rowTotal,
                BadgePosition.Center => (outputWidth - rowTotal) / 2.0,
                _ => inset,
            };

            foreach (var pill in packedRow)
            {
                placements.Add(new BadgePillPlacement(
                    pill.Selector,
                    pill.Text,
                    false,
                    index,
                    x,
                    rowTops[index],
                    pill.Width,
                    pillHeight));
                x += pill.Width + pillGap;
            }
        }
    }

    private static double[] ComputeRowTops(
        int usedRows,
        int outputHeight,
        double inset,
        double pillHeight,
        double rowGap,
        BadgePosition position)
    {
        var tops = new double[usedRows];
        if (IsTopAnchor(position))
        {
            for (var row = 0; row < usedRows; row++)
            {
                tops[row] = inset + (row * (pillHeight + rowGap));
            }
        }
        else if (position == BadgePosition.Center)
        {
            var blockTop = CenteredBlockTop(usedRows, outputHeight, pillHeight, rowGap);
            for (var row = 0; row < usedRows; row++)
            {
                tops[row] = blockTop + (row * (pillHeight + rowGap));
            }
        }
        else
        {
            for (var row = 0; row < usedRows; row++)
            {
                tops[row] = BottomRowTop(row, outputHeight, inset, pillHeight, rowGap);
            }
        }

        return tops;
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
        double safeHeight,
        BadgePosition position)
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

        // The status pill is top-right except when the rail is anchored
        // top-right, where it moves top-left so the two never overlap.
        var x = position == BadgePosition.TopRight
            ? inset
            : outputWidth - inset - pillWidth;
        return new BadgePillPlacement(statusValue.Selector, fitted, true, -1, x, inset, pillWidth, pillHeight);
    }

    private static bool Fits(
        int row,
        double pillWidth,
        int[] rowCounts,
        double[] rowWidths,
        double pillGap,
        double[] rowAvailableWidth)
    {
        if (rowCounts[row] >= BadgeGeometry.MaximumPillsPerRow)
        {
            return false;
        }

        var needed = rowCounts[row] == 0
            ? pillWidth
            : rowWidths[row] + pillGap + pillWidth;
        return needed <= rowAvailableWidth[row];
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

    private readonly record struct PackedPill(BadgeSelector Selector, string Text, double Width);
}
