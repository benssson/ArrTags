using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 12 task 12.3 checks for the configurable global badge position and
/// size (ADR-019 clauses 1-5 and 7). They cover the per-anchor geometry, the
/// preset size factor, rail row stacking and alignment, the derived status-pill
/// placement (including the top-right anchor moving it top-left), and the
/// safe-area bound that no pill paints outside the inset. The layout engine is
/// measured through a delegate, so these tests do not need a raster library.
/// </summary>
public class BadgeSizePositionTests
{
    private static readonly Func<string, float> TenPerCharacter = text => text.Length * 10f;

    private static readonly BadgeValue Status = new(BadgeSelector.UpgradePending, "UPGRADE");

    [Fact]
    public void DefaultPolicyReproducesTheV1BottomLeftGeometry()
    {
        var layout = Build(new[] { new BadgeValue(BadgeSelector.Quality, "aa") }, null, 1000, 1500);

        Assert.Equal(1.0, layout.Scale, 6);
        var pill = Assert.Single(layout.TechnicalPills);
        Assert.Equal(24.0, pill.X, 3);
        Assert.Equal(1500.0 - 24.0 - 48.0, pill.Y, 3);
        Assert.Equal(48.0, pill.Height, 3);
        Assert.Equal(0, pill.Row);
    }

    [Theory]
    [InlineData(BadgePosition.BottomLeft, 24.0, 1428.0)]
    [InlineData(BadgePosition.BottomRight, 932.0, 1428.0)]
    [InlineData(BadgePosition.TopLeft, 24.0, 24.0)]
    [InlineData(BadgePosition.TopRight, 932.0, 24.0)]
    [InlineData(BadgePosition.Center, 478.0, 726.0)]
    public void EachAnchorPlacesTheRailAtTheExpectedCornerOrCenter(BadgePosition position, double expectedX, double expectedY)
    {
        var layout = Build(new[] { new BadgeValue(BadgeSelector.Quality, "aa") }, null, 1000, 1500, position);

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.Equal(expectedX, pill.X, 3);
        Assert.Equal(expectedY, pill.Y, 3);
        Assert.Equal(44.0, pill.Width, 3);
        Assert.Equal(48.0, pill.Height, 3);
    }

    [Theory]
    [InlineData(BadgeSize.Small, 0.75, 36.0)]
    [InlineData(BadgeSize.Medium, 1.0, 48.0)]
    [InlineData(BadgeSize.Large, 1.5, 72.0)]
    public void EachPresetSizeScalesTheGeometryByItsCodeOwnedFactor(BadgeSize size, double expectedScale, double expectedHeight)
    {
        var layout = Build(
            new[] { new BadgeValue(BadgeSelector.Quality, "aa") },
            null,
            1000,
            1500,
            BadgePosition.BottomLeft,
            size);

        Assert.Equal(expectedScale, layout.Scale, 6);
        var pill = Assert.Single(layout.TechnicalPills);
        Assert.Equal(expectedHeight, pill.Height, 3);
    }

    [Fact]
    public void SizeFactorIsCodeOwned()
    {
        Assert.Equal(0.75, BadgeGeometry.SizeFactor(BadgeSize.Small), 6);
        Assert.Equal(1.0, BadgeGeometry.SizeFactor(BadgeSize.Medium), 6);
        Assert.Equal(1.5, BadgeGeometry.SizeFactor(BadgeSize.Large), 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => BadgeGeometry.SizeFactor((BadgeSize)99));
    }

    [Fact]
    public void TopAnchorsStackRowsDownwardFromTheTopEdge()
    {
        var layout = Build(CustomValues(5), null, 1000, 1500, BadgePosition.TopLeft);

        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, layout.TechnicalPills.Select(pill => pill.Row));
        Assert.Equal(24.0, layout.TechnicalPills.First(pill => pill.Row == 0).Y, 3);
        Assert.Equal(24.0 + 48.0 + 8.0, layout.TechnicalPills.First(pill => pill.Row == 1).Y, 3);
    }

    [Fact]
    public void BottomAnchorsStackRowsUpwardFromTheBottomEdge()
    {
        var layout = Build(CustomValues(5), null, 1000, 1500, BadgePosition.BottomLeft);

        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, layout.TechnicalPills.Select(pill => pill.Row));
        Assert.Equal(1500.0 - 24.0 - 48.0, layout.TechnicalPills.First(pill => pill.Row == 0).Y, 3);
        Assert.Equal(1500.0 - 24.0 - 48.0 - 48.0 - 8.0, layout.TechnicalPills.First(pill => pill.Row == 1).Y, 3);
    }

    [Fact]
    public void LeftAnchorsAlignRowsToTheLeftEdge()
    {
        var layout = Build(CustomValues(2), null, 1000, 1500, BadgePosition.BottomLeft);

        var first = layout.TechnicalPills[0];
        var second = layout.TechnicalPills[1];
        Assert.Equal(24.0, first.X, 3);
        Assert.Equal(first.X + first.Width + 8.0, second.X, 3);
    }

    [Fact]
    public void RightAnchorsAlignRowsToTheRightEdge()
    {
        var layout = Build(CustomValues(2), null, 1000, 1500, BadgePosition.BottomRight);

        var first = layout.TechnicalPills[0];
        var second = layout.TechnicalPills[1];
        Assert.Equal(1000.0 - 24.0, second.X + second.Width, 3);
        Assert.Equal(second.X - 8.0, first.X + first.Width, 3);
    }

    [Fact]
    public void CenterAnchorCentersRowsHorizontallyAndTheBlockVertically()
    {
        var layout = Build(CustomValues(2), null, 1000, 1500, BadgePosition.Center);

        var first = layout.TechnicalPills[0];
        var second = layout.TechnicalPills[1];
        var rowWidth = second.X + second.Width - first.X;
        Assert.Equal((1000.0 - rowWidth) / 2.0, first.X, 3);
        Assert.Equal(first.Y, second.Y, 3);

        // A single centered row of height 48 sits at (1500 - 48) / 2.
        Assert.Equal((1500.0 - 48.0) / 2.0, first.Y, 3);
    }

    [Fact]
    public void CenterAnchorStatusBandDropsTheRowsThatWouldStartInsideIt()
    {
        // On a 240 px poster two centered rows would have a block top of
        // (240 - 104) / 2 = 68, inside the 80 px top status band
        // (inset 24 + pill height 48 + row gap 8). The Center reduction drops
        // the block to a single row whose top is (240 - 48) / 2 = 96, so only
        // the first three pills are placed and they clear the status pill.
        var layout = Build(CustomValues(6), Status, 1000, 240, BadgePosition.Center);

        var status = Assert.IsType<BadgePillPlacement>(layout.StatusPill);
        Assert.Equal(3, layout.TechnicalPills.Count);
        Assert.All(layout.TechnicalPills, pill => Assert.Equal(0, pill.Row));
        Assert.All(layout.TechnicalPills, pill => Assert.Equal(96.0, pill.Y, 3));
        Assert.All(
            layout.TechnicalPills,
            pill => Assert.True(pill.Y >= status.Y + status.Height));
    }

    [Theory]
    [InlineData(BadgePosition.BottomLeft)]
    [InlineData(BadgePosition.BottomRight)]
    [InlineData(BadgePosition.TopLeft)]
    [InlineData(BadgePosition.Center)]
    public void StatusPillStaysTopRightForEveryAnchorExceptTopRight(BadgePosition position)
    {
        var layout = Build(Array.Empty<BadgeValue>(), Status, 1000, 1500, position);

        var status = Assert.IsType<BadgePillPlacement>(layout.StatusPill);
        Assert.True(status.IsStatus);
        Assert.Equal(-1, status.Row);
        Assert.Equal(24.0, status.Y, 3);
        Assert.Equal(1000.0 - 24.0, status.X + status.Width, 3);
    }

    [Fact]
    public void TopRightAnchorMovesTheStatusPillToTheTopLeft()
    {
        var layout = Build(Array.Empty<BadgeValue>(), Status, 1000, 1500, BadgePosition.TopRight);

        var status = Assert.IsType<BadgePillPlacement>(layout.StatusPill);
        Assert.Equal(24.0, status.X, 3);
        Assert.Equal(24.0, status.Y, 3);
    }

    [Theory]
    [InlineData(BadgePosition.BottomLeft)]
    [InlineData(BadgePosition.BottomRight)]
    [InlineData(BadgePosition.TopLeft)]
    [InlineData(BadgePosition.TopRight)]
    [InlineData(BadgePosition.Center)]
    public void StatusPillNeverOverlapsTheRailForEveryAnchor(BadgePosition position)
    {
        foreach (var (width, height) in new[] { (1000, 1500), (1000, 300), (320, 480) })
        {
            var layout = Build(CustomValues(5), Status, width, height, position);
            if (layout.StatusPill is not BadgePillPlacement status)
            {
                continue;
            }

            foreach (var pill in layout.TechnicalPills)
            {
                Assert.False(
                    Overlaps(status, pill),
                    $"The status pill overlaps {pill.Selector} at {position} on {width}x{height}.");
            }
        }
    }

    [Theory]
    [InlineData(BadgePosition.BottomLeft)]
    [InlineData(BadgePosition.BottomRight)]
    [InlineData(BadgePosition.TopLeft)]
    [InlineData(BadgePosition.TopRight)]
    [InlineData(BadgePosition.Center)]
    public void TopAnchorsReserveTheTopRowForTheStatusPill(BadgePosition position)
    {
        var layout = Build(CustomValues(6), Status, 1000, 1500, position);
        var status = Assert.IsType<BadgePillPlacement>(layout.StatusPill);

        // The status pill sits in the top band; any top-anchor rail pill in that
        // band must be horizontally clear of it.
        var topBandPills = layout.TechnicalPills.Where(pill => pill.Y < status.Y + status.Height);
        foreach (var pill in topBandPills)
        {
            Assert.False(Overlaps(status, pill));
        }
    }

    [Theory]
    [InlineData(BadgePosition.TopLeft)]
    [InlineData(BadgePosition.TopRight)]
    public void TopAnchorsReserveTheFirstRowForAWideStatusBand(BadgePosition position)
    {
        // A deliberately wide probe: the status pill measures 94 px and the one
        // technical pill measures 900 px on a 1000 px poster, so the reserved
        // first row (952 - 94 - 8 = 850 px) cannot hold the technical pill and
        // it must drop to the second row. Without the reservation the 900 px
        // pill would fit the first row and overlap the status pill.
        var measure = new Func<string, float>(text => text == Status.Text ? 70f : 876f);
        var layout = BadgeLayoutEngine.Build(
            new[] { new BadgeValue(BadgeSelector.CustomBadge, "WIDE") },
            Status,
            1000,
            1500,
            Policy(position, BadgeSize.Medium),
            measure);

        var status = Assert.IsType<BadgePillPlacement>(layout.StatusPill);
        var pill = Assert.Single(layout.TechnicalPills);

        // The reservation is what keeps the technical pill below the status band.
        Assert.True(pill.Y >= status.Y + status.Height);
        Assert.False(Overlaps(status, pill));

        // Both pills stay inside the scaled safe area.
        var inset = BadgeGeometry.OuterInset * layout.Scale;
        Assert.InRange(pill.X, inset - 0.001, 1000.0 - inset + 0.001);
        Assert.InRange(pill.X + pill.Width, inset - 0.001, 1000.0 - inset + 0.001);
        Assert.InRange(status.X, inset - 0.001, 1000.0 - inset + 0.001);
        Assert.InRange(status.X + status.Width, inset - 0.001, 1000.0 - inset + 0.001);
    }

    [Theory]
    [InlineData(BadgePosition.BottomLeft)]
    [InlineData(BadgePosition.BottomRight)]
    [InlineData(BadgePosition.TopLeft)]
    [InlineData(BadgePosition.TopRight)]
    [InlineData(BadgePosition.Center)]
    public void NoPillPaintsOutsideTheSafeAreaOnNarrowAndShortPosters(BadgePosition position)
    {
        var sizes = new[] { BadgeSize.Small, BadgeSize.Medium, BadgeSize.Large };
        var dimensions = new[] { (240, 600), (320, 480), (500, 750), (1000, 150), (1000, 1500), (2000, 3000) };

        foreach (var size in sizes)
        {
            foreach (var (width, height) in dimensions)
            {
                var layout = Build(CustomValues(6), Status, width, height, position, size);
                var scale = BadgeGeometry.ComputeEffectiveScale(width, height, Policy(position, size));
                var inset = BadgeGeometry.OuterInset * scale;

                foreach (var pill in layout.TechnicalPills.Concat(layout.StatusPill is null ? Array.Empty<BadgePillPlacement>() : new[] { layout.StatusPill! }))
                {
                    Assert.InRange(pill.X, inset - 0.001, width - inset + 0.001);
                    Assert.InRange(pill.X + pill.Width, inset - 0.001, width - inset + 0.001);
                    Assert.InRange(pill.Y, inset - 0.001, height - inset + 0.001);
                    Assert.InRange(pill.Y + pill.Height, inset - 0.001, height - inset + 0.001);
                }
            }
        }
    }

    [Fact]
    public void LargeSizeOmitsRatherThanOverflowsOnAShortPoster()
    {
        var layout = Build(CustomValues(6), Status, 1000, 120, BadgePosition.TopLeft, BadgeSize.Large);

        var scale = BadgeGeometry.ComputeEffectiveScale(1000, 120, Policy(BadgePosition.TopLeft, BadgeSize.Large));
        var inset = BadgeGeometry.OuterInset * scale;
        foreach (var pill in layout.TechnicalPills)
        {
            Assert.InRange(pill.Y, inset - 0.001, 120 - inset + 0.001);
            Assert.InRange(pill.Y + pill.Height, inset - 0.001, 120 - inset + 0.001);
        }
    }

    [Theory]
    [InlineData(BadgeSize.Medium, 3000, 120, 120.0 / 48.0)]
    [InlineData(BadgeSize.Large, 1000, 40, 40.0 / 48.0)]
    [InlineData(BadgeSize.Small, 1000, 30, 30.0 / 48.0)]
    public void SafeAreaClampReducesTheEffectiveScaleOnAShortPoster(
        BadgeSize size, int width, int height, double expectedScale)
    {
        var policy = Policy(BadgePosition.BottomLeft, size);

        var unclamped = BadgeGeometry.ComputeScale(width, policy) * BadgeGeometry.SizeFactor(size);
        var effective = BadgeGeometry.ComputeEffectiveScale(width, height, policy);

        // The clamp binds: the unclamped scale would consume more than the whole poster.
        Assert.True(unclamped > expectedScale + 0.000001);
        Assert.Equal(expectedScale, effective, 6);
        Assert.Equal(Math.Min(width, height) / (2.0 * BadgeGeometry.OuterInset), effective, 6);

        // The scaled inset cannot consume more than the whole poster, so the safe
        // area stays non-negative (exactly zero at the clamp boundary).
        var scaledInset = BadgeGeometry.OuterInset * effective;
        Assert.True(Math.Min(width, height) - (2.0 * scaledInset) >= -0.000001);
    }

    private static RenderOutputPolicy Policy(BadgePosition position, BadgeSize size)
    {
        return new RenderOutputPolicy { Position = position, Size = size };
    }

    private static IReadOnlyList<BadgeValue> CustomValues(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new BadgeValue(
                BadgeSelector.CustomBadge,
                "v" + index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static BadgeLayout Build(
        IReadOnlyList<BadgeValue> values,
        BadgeValue? status,
        int width,
        int height,
        BadgePosition position = BadgePosition.BottomLeft,
        BadgeSize size = BadgeSize.Medium)
    {
        return BadgeLayoutEngine.Build(
            values,
            status,
            width,
            height,
            Policy(position, size),
            TenPerCharacter);
    }

    private static bool Overlaps(BadgePillPlacement first, BadgePillPlacement second)
    {
        const double epsilon = 0.001;
        return first.X + first.Width - epsilon > second.X
            && second.X + second.Width - epsilon > first.X
            && first.Y + first.Height - epsilon > second.Y
            && second.Y + second.Height - epsilon > first.Y;
    }
}
