using System;
using System.Linq;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.6 truncation behavior. A visible label is bounded to 24
/// Unicode scalar values including the ellipsis, longer text is end-truncated to
/// a 21-scalar prefix plus the marker, whitespace and control scalars are
/// normalized first, a label that is still too wide for its pill is shortened
/// further and then omitted, and no surrogate pair is ever split. These assertions
/// exercise the pipeline from resolved metadata through normalization and layout.
/// </summary>
public class RendererBehaviorTruncationTests
{
    [Fact]
    public void AResolvedLongCustomValueIsHardLimitedToTwentyFourScalars()
    {
        var label = new string('a', 40);
        var metadata = RendererBehaviorFixtures.BuildMetadata(customBadges: new[] { label });

        var selection = BadgeDefinitionResolver.Resolve(metadata, BadgeDefinition.V1Default);
        var custom = Assert.Single(selection.TechnicalValues, value => value.Selector == BadgeSelector.CustomBadge);

        var normalized = BadgeTextNormalizer.Normalize(custom.Text, RenderOutputPolicy.Default);

        Assert.True(normalized.WasTruncated);
        Assert.Equal(new string('a', 21) + "...", normalized.Text);
        Assert.Equal(24, RendererBehaviorFixtures.ScalarCount(normalized.Text!));
    }

    [Fact]
    public void AWithinLimitLabelIsUnchanged()
    {
        var label = new string('b', 24);
        var metadata = RendererBehaviorFixtures.BuildMetadata(customBadges: new[] { label });

        var selection = BadgeDefinitionResolver.Resolve(metadata, BadgeDefinition.V1Default);
        var custom = Assert.Single(selection.TechnicalValues, value => value.Selector == BadgeSelector.CustomBadge);

        var normalized = BadgeTextNormalizer.Normalize(custom.Text, RenderOutputPolicy.Default);

        Assert.False(normalized.WasTruncated);
        Assert.Equal(label, normalized.Text);
    }

    [Fact]
    public void WhitespaceAndControlScalarsAreNormalizedBeforeTheLimitIsApplied()
    {
        var normalized = BadgeTextNormalizer.Normalize(
            "  WEB\u0000-DL\t\t1080p  ",
            RenderOutputPolicy.Default);

        Assert.False(normalized.IsOmitted);
        Assert.Equal("WEB-DL 1080p", normalized.Text);
        Assert.False(normalized.WasTruncated);
    }

    [Fact]
    public void AValueThatCannotFitAnyPillIsOmittedWithoutExpandingLowerPriorityValues()
    {
        // The first value can never fit because even its shortest shortened form
        // carries an extremely wide scalar; the second value must survive with its
        // own text and must not be expanded to recover the omitted one.
        var values = new[]
        {
            new BadgeValue(BadgeSelector.Quality, "\u1671\u1671\u1671"),
            new BadgeValue(BadgeSelector.Resolution, "ok"),
        };

        var layout = BadgeLayoutEngine.Build(
            values,
            null,
            1000,
            1500,
            RenderOutputPolicy.Default,
            text => text.Contains("\u1671", StringComparison.Ordinal) ? 100000f : text.Length * 10f);

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.Equal(BadgeSelector.Resolution, pill.Selector);
        Assert.Equal("ok", pill.Text);
    }

    [Fact]
    public void AnAllowlistedValueThatCannotFitStillFollowsTheOmitBehavior()
    {
        // The allowlist filter runs before layout and must not bypass the fit
        // logic: an allowlisted value that cannot fit any pill is still omitted.
        var label = "\u1671\u1671\u1671";
        var metadata = RendererBehaviorFixtures.BuildMetadata(customBadges: new[] { label });
        var definitions = new[]
        {
            new BadgeDefinition(
                BadgeSelector.CustomBadge,
                true,
                BadgeDefinition.ValuePlaceholder,
                new[] { label }),
        };

        var selection = BadgeDefinitionResolver.Resolve(metadata, definitions);
        Assert.Single(selection.TechnicalValues);

        var layout = BadgeLayoutEngine.Build(
            selection.TechnicalValues,
            null,
            1000,
            1500,
            RenderOutputPolicy.Default,
            text => text.Contains("\u1671", StringComparison.Ordinal) ? 100000f : text.Length * 10f);

        Assert.Empty(layout.TechnicalPills);
    }

    [Fact]
    public void WidthFittingFurtherShortensAnOverwideLabelAndKeepsTheScalarBound()
    {
        var label = new string('W', 16) + string.Concat(Enumerable.Repeat("\u1671", 8));
        Assert.Equal(24, RendererBehaviorFixtures.ScalarCount(label));

        var layout = BadgeLayoutEngine.Build(
            new[] { new BadgeValue(BadgeSelector.Quality, label) },
            null,
            1000,
            1500,
            RenderOutputPolicy.Default,
            text =>
            {
                var width = 0f;
                foreach (var rune in text.EnumerateRunes())
                {
                    width += rune.Value == 0x1671 ? 100f : 10f;
                }

                return width;
            });

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.EndsWith("...", pill.Text, StringComparison.Ordinal);
        Assert.NotEqual(label, pill.Text);
        Assert.True(RendererBehaviorFixtures.ScalarCount(pill.Text) <= RenderOutputPolicy.Default.MaximumScalarValues);
    }

    [Fact]
    public void WidthFittingNeverSplitsASupplementaryScalar()
    {
        var label = string.Concat(Enumerable.Repeat("\U0001F600", 22));
        Assert.Equal(22, RendererBehaviorFixtures.ScalarCount(label));

        var layout = BadgeLayoutEngine.Build(
            new[] { new BadgeValue(BadgeSelector.CustomBadge, label) },
            null,
            1000,
            1500,
            RenderOutputPolicy.Default,
            text => RendererBehaviorFixtures.ScalarCount(text) * 100f);

        var pill = Assert.Single(layout.TechnicalPills);
        Assert.EndsWith("...", pill.Text, StringComparison.Ordinal);
        Assert.True(RendererBehaviorFixtures.ScalarCount(pill.Text) <= RenderOutputPolicy.Default.MaximumScalarValues);
        RendererBehaviorFixtures.AssertWellFormedUtf16(pill.Text);
    }
}
