using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.6 layout behavior. Technical badges form a bottom-left rail of
/// at most two rows with at most three pills per row, overflow moves to the next
/// row, lower-priority candidates are omitted once the rail is full, no pill is
/// split across rows, every pill stays inside the safe area of narrow or short
/// posters, and the independent top-right UPGRADE status pill appears only for a
/// confirmed true value and never overlaps the rail. The layout engine is measured
/// through a delegate, so these tests are unguarded and do not load Skia.
/// </summary>
public class RendererBehaviorLayoutTests
{
    [Fact]
    public void UpgradeStatusIsResolvedOnlyWhenItIsConfirmedTrue()
    {
        Assert.NotNull(BadgeDefinitionResolver.Resolve(
            RendererBehaviorFixtures.BuildMetadata(upgradePending: true),
            BadgeDefinition.V1Default).StatusValue);
        Assert.Null(BadgeDefinitionResolver.Resolve(
            RendererBehaviorFixtures.BuildMetadata(upgradePending: false),
            BadgeDefinition.V1Default).StatusValue);
        Assert.Null(BadgeDefinitionResolver.Resolve(
            RendererBehaviorFixtures.BuildMetadata(upgradePending: null),
            BadgeDefinition.V1Default).StatusValue);
    }

    [Fact]
    public void TheRailPacksThreePillsPerRowAcrossTwoRows()
    {
        var values = CustomValues(6);

        var layout = Build(values, null, 1000, 1500);

        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1 }, layout.TechnicalPills.Select(pill => pill.Row));
        Assert.All(layout.TechnicalPills, pill => Assert.True(pill.Row <= 1));
    }

    [Fact]
    public void LowerPriorityCandidatesAreOmittedOnceBothRowsAreFull()
    {
        var values = CustomValues(8);

        var layout = Build(values, null, 1000, 1500);

        Assert.Equal(6, layout.TechnicalPills.Count);
        Assert.Equal(
            new[] { "v1", "v2", "v3", "v4", "v5", "v6" },
            layout.TechnicalPills.Select(pill => pill.Text));
    }

    [Fact]
    public void NoPillIsSplitAcrossRowsAndRowsFollowPriorityOrder()
    {
        var values = CustomValues(5);

        var layout = Build(values, null, 1000, 1500);

        var rows = layout.TechnicalPills.Select(pill => pill.Row).ToArray();
        for (var index = 1; index < rows.Length; index++)
        {
            Assert.True(rows[index] >= rows[index - 1]);
        }

        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, rows);
    }

    [Theory]
    [InlineData(240, 600)]
    [InlineData(320, 480)]
    [InlineData(1000, 150)]
    [InlineData(1000, 1500)]
    [InlineData(2000, 3000)]
    public void EveryRailPillStaysInsideTheSafeArea(int width, int height)
    {
        var layout = Build(CustomValues(6), null, width, height);
        var scale = BadgeGeometry.ComputeScale(width, RenderOutputPolicy.Default);
        var inset = BadgeGeometry.OuterInset * scale;

        Assert.NotEmpty(layout.TechnicalPills);
        foreach (var pill in layout.TechnicalPills)
        {
            Assert.InRange(pill.X, inset - 0.001, width - inset + 0.001);
            Assert.InRange(pill.X + pill.Width, inset - 0.001, width - inset + 0.001);
            Assert.InRange(pill.Y, inset - 0.001, height - inset + 0.001);
            Assert.InRange(pill.Y + pill.Height, inset - 0.001, height - inset + 0.001);
        }
    }

    [Theory]
    [InlineData(320, 480)]
    [InlineData(1000, 150)]
    [InlineData(1000, 1500)]
    [InlineData(2000, 3000)]
    public void TheStatusPillIsTopRightAndNeverOverlapsTheRail(int width, int height)
    {
        var layout = Build(CustomValues(5), new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE"), width, height);
        var scale = BadgeGeometry.ComputeScale(width, RenderOutputPolicy.Default);
        var inset = BadgeGeometry.OuterInset * scale;

        var status = Assert.IsType<BadgePillPlacement>(layout.StatusPill);
        Assert.True(status.IsStatus);
        Assert.Equal(-1, status.Row);
        Assert.Equal(inset, status.Y, 3);
        Assert.Equal(width - inset, status.X + status.Width, 3);

        foreach (var pill in layout.TechnicalPills)
        {
            Assert.False(Overlaps(status, pill), $"The status pill overlaps {pill.Selector} on {width}x{height}.");
        }
    }

    [Fact]
    public void TheTechnicalRailSitsAtTheBottomLeft()
    {
        var layout = Build(CustomValues(4), null, 1000, 1500);
        var scale = BadgeGeometry.ComputeScale(1000, RenderOutputPolicy.Default);
        var inset = BadgeGeometry.OuterInset * scale;
        var pillHeight = BadgeGeometry.PillHeight * scale;

        var first = layout.TechnicalPills[0];
        Assert.Equal(inset, first.X, 3);
        Assert.Equal(1500 - inset - pillHeight, first.Y, 3);

        var secondRow = layout.TechnicalPills.First(pill => pill.Row == 1);
        Assert.Equal(1500 - inset - (2 * pillHeight) - (BadgeGeometry.RowGap * scale), secondRow.Y, 3);
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
        int height)
    {
        return BadgeLayoutEngine.Build(
            values,
            status,
            width,
            height,
            RenderOutputPolicy.Default,
            text => text.Length * 10f);
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
