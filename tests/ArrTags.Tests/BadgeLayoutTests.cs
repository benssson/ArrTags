using System;
using System.Globalization;
using System.Linq;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the pure ADR-009 rail layout engine: priority
/// order, two-row three-pill packing, overflow to the next row, omission once the
/// rail is full, end-truncation width fitting, status placement, and scale
/// clamping. The engine is measured through a delegate, so these tests do not
/// need a raster library.
/// </summary>
public class BadgeLayoutTests
{
    private static readonly Func<string, float> TenPerCharacter = text => text.Length * 10f;

    [Fact]
    public void PriorityOrderIsPreservedAcrossPacking()
    {
        var values = new[]
        {
            new BadgeValue(BadgeSelector.Quality, "q"),
            new BadgeValue(BadgeSelector.Resolution, "r"),
            new BadgeValue(BadgeSelector.Source, "s"),
            new BadgeValue(BadgeSelector.VideoCodec, "c"),
        };

        var layout = Build(values, null, TenPerCharacter);

        Assert.Equal(
            new[] { BadgeSelector.Quality, BadgeSelector.Resolution, BadgeSelector.Source, BadgeSelector.VideoCodec },
            layout.TechnicalPills.Select(pill => pill.Selector));
    }

    [Fact]
    public void AtMostThreePillsPerRowAndTwoRowsAreUsed()
    {
        var values = Enumerable.Range(1, 5)
            .Select(index => new BadgeValue(BadgeSelector.CustomBadge, "v" + index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        var layout = Build(values, null, TenPerCharacter);

        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, layout.TechnicalPills.Select(pill => pill.Row));
    }

    [Fact]
    public void LowerPriorityCandidatesAreOmittedOnceBothRowsAreFull()
    {
        var values = Enumerable.Range(1, 7)
            .Select(index => new BadgeValue(BadgeSelector.CustomBadge, "v" + index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        var layout = Build(values, null, TenPerCharacter);

        Assert.Equal(6, layout.TechnicalPills.Count);
        Assert.Equal("v6", layout.TechnicalPills[^1].Text);
    }

    [Fact]
    public void PillThatDoesNotFitTheRemainingRowMovesToTheNextRow()
    {
        var values = new[]
        {
            new BadgeValue(BadgeSelector.Quality, "400"),
            new BadgeValue(BadgeSelector.Resolution, "400"),
            new BadgeValue(BadgeSelector.Source, "200"),
        };

        var layout = Build(values, null, text => int.Parse(text, CultureInfo.InvariantCulture));

        Assert.Equal(new[] { 0, 0, 1 }, layout.TechnicalPills.Select(pill => pill.Row));
        Assert.All(layout.TechnicalPills, pill => Assert.True(pill.Width <= 952.0));
    }

    [Fact]
    public void TechnicalPillsArePackedLeftToRightInsideTheSafeArea()
    {
        var values = new[]
        {
            new BadgeValue(BadgeSelector.Quality, "aa"),
            new BadgeValue(BadgeSelector.Resolution, "bb"),
        };

        var layout = Build(values, null, TenPerCharacter);

        var first = layout.TechnicalPills[0];
        var second = layout.TechnicalPills[1];
        Assert.Equal(24.0, first.X, 3);
        Assert.Equal(first.X + first.Width + 8.0, second.X, 3);
        Assert.Equal(first.Y, second.Y, 3);
    }

    [Fact]
    public void TechnicalRailSitsAtTheBottomInsideTheSafeArea()
    {
        var values = new[] { new BadgeValue(BadgeSelector.Quality, "aa") };

        var layout = Build(values, null, TenPerCharacter);

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.Equal(1500.0 - 24.0 - 48.0, pill.Y, 3);
        Assert.Equal(48.0, pill.Height, 3);
    }

    [Fact]
    public void TextThatDoesNotFitAFullRowIsShortenedWithTheEllipsis()
    {
        var values = new[] { new BadgeValue(BadgeSelector.Quality, "123456789012345678901234567890") };

        var layout = Build(values, null, text => text.Length * 100f);

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.True(pill.Text.Length < 24);
        Assert.EndsWith("...", pill.Text, StringComparison.Ordinal);
        Assert.True(pill.Width <= 952.0);
    }

    [Fact]
    public void WidthFittingKeepsFinalTextWithinTheAdr009ScalarLimit()
    {
        // Reviewer reproduction: 24 scalars made of 16 narrow 'W' plus 8 wide
        // U+1671 glyphs. The full label is too wide, but dropping the wide
        // trailing glyphs and appending "..." fits, so width fitting must not
        // grow the final text past the 24-scalar ADR-009 limit.
        var label = new string('W', 16) + string.Concat(Enumerable.Repeat("\u1671", 8));
        Assert.Equal(24, ScalarCount(label));

        var values = new[] { new BadgeValue(BadgeSelector.Quality, label) };

        var layout = Build(values, null, ReviewerMeasure);

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.EndsWith("...", pill.Text, StringComparison.Ordinal);
        Assert.NotEqual(label, pill.Text);
        Assert.True(
            ScalarCount(pill.Text) <= RenderOutputPolicy.Default.MaximumScalarValues,
            $"Final pill text '{pill.Text}' has {ScalarCount(pill.Text)} scalars.");
    }

    [Fact]
    public void WidthFittingStillShortensFurtherUnderMoreWidthPressure()
    {
        var label = new string('W', 16) + string.Concat(Enumerable.Repeat("\u1671", 8));
        var values = new[] { new BadgeValue(BadgeSelector.Quality, label) };

        var layout = Build(values, null, text => ReviewerMeasure(text) * 10f);

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.EndsWith("...", pill.Text, StringComparison.Ordinal);
        Assert.True(ScalarCount(pill.Text) < 24);
        Assert.True(ScalarCount(pill.Text) <= RenderOutputPolicy.Default.MaximumScalarValues);
    }

    [Fact]
    public void ValueThatCannotProduceAVisibleLabelIsOmitted()
    {
        var values = new[] { new BadgeValue(BadgeSelector.Quality, "1080p") };

        var layout = Build(values, null, _ => 100000f);

        Assert.True(layout.IsEmpty);
    }

    [Fact]
    public void StatusPillIsPlacedAtTheTopRight()
    {
        var status = new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE");

        var layout = Build(Array.Empty<BadgeValue>(), status, TenPerCharacter);

        Assert.NotNull(layout.StatusPill);
        var pill = layout.StatusPill!;
        Assert.True(pill.IsStatus);
        Assert.Equal(-1, pill.Row);
        Assert.Equal(24.0, pill.Y, 3);
        Assert.Equal(1000.0 - 24.0 - pill.Width, pill.X, 3);
    }

    [Fact]
    public void StatusPillIsIndependentOfTheTechnicalRail()
    {
        var values = new[] { new BadgeValue(BadgeSelector.Quality, "1080p") };
        var status = new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE");

        var layout = Build(values, status, TenPerCharacter);

        Assert.Single(layout.TechnicalPills);
        Assert.NotNull(layout.StatusPill);
        Assert.DoesNotContain(layout.TechnicalPills, pill => pill.IsStatus);
    }

    [Theory]
    [InlineData(100, 0.5)]
    [InlineData(500, 0.5)]
    [InlineData(1000, 1.0)]
    [InlineData(2000, 2.0)]
    [InlineData(5000, 4.0)]
    public void ScaleIsClampedToThePolicyBounds(int outputWidth, double expectedScale)
    {
        var layout = BadgeLayoutEngine.Build(
            Array.Empty<BadgeValue>(),
            null,
            outputWidth,
            1500,
            RenderOutputPolicy.Default,
            TenPerCharacter);

        Assert.Equal(expectedScale, layout.Scale, 6);
    }

    [Fact]
    public void ShortPosterOmitsRowsThatWouldLeaveTheSafeArea()
    {
        var values = new[]
        {
            new BadgeValue(BadgeSelector.Quality, "1080p"),
            new BadgeValue(BadgeSelector.Resolution, "x"),
        };

        var layout = BadgeLayoutEngine.Build(values, null, 1000, 100, RenderOutputPolicy.Default, TenPerCharacter);

        Assert.NotEmpty(layout.TechnicalPills);
        Assert.All(layout.TechnicalPills, pill => Assert.Equal(0, pill.Row));
        Assert.All(
            layout.TechnicalPills,
            pill => Assert.True(pill.Y >= 24.0 && pill.Y + pill.Height <= 76.0));
    }

    [Fact]
    public void StatusPillIsOmittedWhenItDoesNotFitTheSafeArea()
    {
        var status = new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE");

        var layout = BadgeLayoutEngine.Build(
            Array.Empty<BadgeValue>(),
            status,
            1000,
            60,
            RenderOutputPolicy.Default,
            TenPerCharacter);

        Assert.Null(layout.StatusPill);
    }

    [Fact]
    public void StatusAndTechnicalRailDoNotOverlapOnAShortPoster()
    {
        var values = new[] { new BadgeValue(BadgeSelector.Quality, "1080p") };
        var status = new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE");

        var layout = BadgeLayoutEngine.Build(values, status, 1000, 150, RenderOutputPolicy.Default, TenPerCharacter);

        Assert.NotNull(layout.StatusPill);
        Assert.Empty(layout.TechnicalPills);
    }

    [Fact]
    public void StatusAndTwoRailRowsCoexistWithoutOverlapOnATallPoster()
    {
        var values = Enumerable.Range(1, 5)
            .Select(index => new BadgeValue(BadgeSelector.CustomBadge, "v" + index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        var status = new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE");

        var layout = BadgeLayoutEngine.Build(values, status, 1000, 300, RenderOutputPolicy.Default, TenPerCharacter);

        Assert.NotNull(layout.StatusPill);
        Assert.Equal(2, layout.TechnicalPills.Max(pill => pill.Row) + 1);
        var topmost = layout.TechnicalPills.OrderBy(pill => pill.Y).First();
        Assert.True(topmost.Y >= layout.StatusPill!.Y + layout.StatusPill.Height);
    }

    [Fact]
    public void ShortenRetainsTheRequestedPrefixPlusEllipsis()
    {
        Assert.Equal("abc...", BadgeTextNormalizer.Shorten("abcdef", 3, "..."));
        Assert.Equal("ab", BadgeTextNormalizer.Shorten("ab", 3, "..."));
        Assert.Equal("\U0001F600...", BadgeTextNormalizer.Shorten("\U0001F600\U0001F600", 1, "..."));
    }

    private static BadgeLayout Build(
        BadgeValue[] values,
        BadgeValue? status,
        Func<string, float> measure)
    {
        return BadgeLayoutEngine.Build(values, status, 1000, 1500, RenderOutputPolicy.Default, measure);
    }

    private static float ReviewerMeasure(string text)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            width += rune.Value switch
            {
                0x0057 => 10f,
                0x1671 => 100f,
                0x002E => 1f,
                _ => 10f,
            };
        }

        return width;
    }

    private static int ScalarCount(string value)
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
