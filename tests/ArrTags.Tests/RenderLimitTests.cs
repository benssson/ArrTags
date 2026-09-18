using System;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.5 checks the pre-decode, pre-draw, and pre-encode limit
/// enforcement boundary. Source byte/dimension limits, the derived output
/// surface bound, and the 24 Unicode scalar-value text limit must all be
/// enforced with safe bounded results and without mutating input.
/// </summary>
public class RenderLimitTests
{
    private static readonly OperationalLimits DefaultLimits = new OperationalLimits();

    [Fact]
    public void NormalizeRemovesNonWhitespaceControlScalars()
    {
        var result = BadgeTextNormalizer.Normalize("Blu\u0007ray\u00011080p", RenderOutputPolicy.Default);

        Assert.False(result.IsOmitted);
        Assert.False(result.WasTruncated);
        Assert.Equal("Bluray1080p", result.Text);
    }

    [Fact]
    public void NormalizeCollapsesWhitespaceRunsToOneSpace()
    {
        var result = BadgeTextNormalizer.Normalize("a   b\t\tc\r\n\nd", RenderOutputPolicy.Default);

        Assert.Equal("a b c d", result.Text);
    }

    [Fact]
    public void NormalizeTrimsLeadingAndTrailingWhitespace()
    {
        var result = BadgeTextNormalizer.Normalize("   WEB-DL   ", RenderOutputPolicy.Default);

        Assert.Equal("WEB-DL", result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("     ")]
    [InlineData("\t\n\r")]
    [InlineData("\u0001\u0002")]
    public void NormalizeOmitsTextWithNoVisibleContent(string text)
    {
        var result = BadgeTextNormalizer.Normalize(text, RenderOutputPolicy.Default);

        Assert.True(result.IsOmitted);
        Assert.Null(result.Text);
        Assert.False(result.WasTruncated);
    }

    [Fact]
    public void NormalizeKeepsTextAtExactlyTheScalarLimit()
    {
        var result = BadgeTextNormalizer.Normalize(new string('a', 24), RenderOutputPolicy.Default);

        Assert.False(result.WasTruncated);
        Assert.Equal(new string('a', 24), result.Text);
        Assert.Equal(24, ScalarCount(result.Text!));
    }

    [Fact]
    public void NormalizeEndTruncatesToRetainedPrefixPlusEllipsis()
    {
        var result = BadgeTextNormalizer.Normalize(new string('a', 30), RenderOutputPolicy.Default);

        Assert.True(result.WasTruncated);
        Assert.Equal(new string('a', 21) + "...", result.Text);
        Assert.Equal(24, ScalarCount(result.Text!));
    }

    [Fact]
    public void NormalizeCountsSupplementaryScalarsAsOne()
    {
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 30));

        var result = BadgeTextNormalizer.Normalize(emoji, RenderOutputPolicy.Default);

        Assert.True(result.WasTruncated);
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 21)) + "...", result.Text);
        Assert.Equal(24, ScalarCount(result.Text!));
        AssertWellFormedUtf16(result.Text!);
    }

    [Fact]
    public void NormalizeNeverSplitsASurrogatePairAtTheTruncationBoundary()
    {
        // 20 BMP scalars, one supplementary scalar (surrogate pair), then more
        // BMP scalars. Retaining the first 21 scalars must include the whole
        // supplementary scalar.
        var text = new string('a', 20) + "\U0001F600" + new string('b', 10);

        var result = BadgeTextNormalizer.Normalize(text, RenderOutputPolicy.Default);

        Assert.True(result.WasTruncated);
        Assert.Equal(new string('a', 20) + "\U0001F600" + "...", result.Text);
        Assert.Equal(24, ScalarCount(result.Text!));
        AssertWellFormedUtf16(result.Text!);
    }

    [Fact]
    public void NormalizeCountsCombiningMarksAsSeparateScalarValues()
    {
        var text = string.Concat(Enumerable.Repeat("e\u0301", 30));

        var result = BadgeTextNormalizer.Normalize(text, RenderOutputPolicy.Default);

        var expectedPrefix = string.Concat(Enumerable.Repeat("e\u0301", 10)) + "e";
        Assert.True(result.WasTruncated);
        Assert.Equal(expectedPrefix + "...", result.Text);
        Assert.Equal(24, ScalarCount(result.Text!));
    }

    [Fact]
    public void NormalizeUsesBoundedPolicyValuesForShorterLimits()
    {
        var policy = new RenderOutputPolicy
        {
            MaximumScalarValues = 6,
            RetainedPrefixScalarValues = 3,
            Ellipsis = "..",
        };

        var result = BadgeTextNormalizer.Normalize(new string('a', 10), policy);

        Assert.True(result.WasTruncated);
        Assert.Equal("aaa..", result.Text);
        Assert.Equal(5, ScalarCount(result.Text!));
    }

    [Fact]
    public void NormalizeRejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => BadgeTextNormalizer.Normalize(null!, RenderOutputPolicy.Default));
        Assert.Throws<ArgumentNullException>(() => BadgeTextNormalizer.Normalize("x", null!));
    }

    [Theory]
    [InlineData(0, 21, "...")]
    [InlineData(24, -1, "...")]
    [InlineData(24, 21, "")]
    [InlineData(3, 21, "....")]
    public void NormalizeRejectsUnusablePolicyLimits(int maximumScalarValues, int retainedPrefix, string ellipsis)
    {
        var policy = new RenderOutputPolicy
        {
            MaximumScalarValues = maximumScalarValues,
            RetainedPrefixScalarValues = retainedPrefix,
            Ellipsis = ellipsis,
        };

        Assert.Throws<ArgumentException>(() => BadgeTextNormalizer.Normalize("test", policy));
    }

    [Fact]
    public void ValidateSourceImageAcceptsWithinLimits()
    {
        var result = RenderLimitGuard.ValidateSourceImage(4L * 1024 * 1024, 1000, 1500, DefaultLimits);

        Assert.True(result.IsAccepted);
        Assert.Equal(RenderLimitReason.None, result.Reason);
    }

    [Fact]
    public void ValidateSourceImageAcceptsExactBoundaries()
    {
        var result = RenderLimitGuard.ValidateSourceImage(
            DefaultLimits.SourceArtifactLimitBytes,
            DefaultLimits.MaxImageDimensionPixels,
            DefaultLimits.MaxImageDimensionPixels,
            DefaultLimits);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void ValidateSourceImageRejectsByteLimitExceeded()
    {
        var result = RenderLimitGuard.ValidateSourceImage(
            DefaultLimits.SourceArtifactLimitBytes + 1,
            1000,
            1500,
            DefaultLimits);

        Assert.True(result.IsRejected);
        Assert.Equal(RenderLimitReason.SourceByteLimitExceeded, result.Reason);
    }

    [Fact]
    public void ValidateSourceImageRejectsDimensionLimitExceeded()
    {
        var result = RenderLimitGuard.ValidateSourceImage(
            1024,
            DefaultLimits.MaxImageDimensionPixels + 1,
            1000,
            DefaultLimits);

        Assert.True(result.IsRejected);
        Assert.Equal(RenderLimitReason.SourceDimensionLimitExceeded, result.Reason);
    }

    [Theory]
    [InlineData(0L, 1000, 1500)]
    [InlineData(-1L, 1000, 1500)]
    [InlineData(1024L, 0, 1500)]
    [InlineData(1024L, 1000, -1)]
    public void ValidateSourceImageRejectsMalformedDescriptors(long byteLength, int width, int height)
    {
        var result = RenderLimitGuard.ValidateSourceImage(byteLength, width, height, DefaultLimits);

        Assert.True(result.IsRejected);
        Assert.Equal(RenderLimitReason.MalformedSource, result.Reason);
    }

    [Fact]
    public void ValidateSourceImageRequiresLimits()
    {
        Assert.Throws<ArgumentNullException>(() => RenderLimitGuard.ValidateSourceImage(1, 1, 1, null!));
    }

    [Fact]
    public void ValidateDerivedOutputAcceptsNormalPoster()
    {
        var result = RenderLimitGuard.ValidateDerivedOutput(1000, 1500, DefaultLimits);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void ValidateDerivedOutputRejectsSurfaceBeyondDerivedLimit()
    {
        // 4000 x 4000 x 4 raw RGBA bytes = 64 MiB, above the 32 MiB default.
        var result = RenderLimitGuard.ValidateDerivedOutput(4000, 4000, DefaultLimits);

        Assert.True(result.IsRejected);
        Assert.Equal(RenderLimitReason.OutputByteLimitExceeded, result.Reason);
    }

    [Fact]
    public void ValidateDerivedOutputRejectsOversizedDimension()
    {
        var result = RenderLimitGuard.ValidateDerivedOutput(
            DefaultLimits.MaxImageDimensionPixels + 1,
            1000,
            DefaultLimits);

        Assert.True(result.IsRejected);
        Assert.Equal(RenderLimitReason.OutputDimensionLimitExceeded, result.Reason);
    }

    [Fact]
    public void ValidateDerivedOutputDoesNotOverflowForExtremeDimensions()
    {
        var result = RenderLimitGuard.ValidateDerivedOutput(int.MaxValue, int.MaxValue, DefaultLimits);

        Assert.True(result.IsRejected);
        Assert.Equal(RenderLimitReason.OutputDimensionLimitExceeded, result.Reason);
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(1000, 0)]
    [InlineData(-1, 1500)]
    public void ValidateDerivedOutputRejectsMalformedDimensions(int width, int height)
    {
        var result = RenderLimitGuard.ValidateDerivedOutput(width, height, DefaultLimits);

        Assert.True(result.IsRejected);
        Assert.Equal(RenderLimitReason.MalformedSource, result.Reason);
    }

    [Fact]
    public void ValidateDerivedOutputUsesTheExactSurfaceBoundary()
    {
        var limits = new OperationalLimits
        {
            DerivedArtifactLimitBytes = 400,
        };

        Assert.True(RenderLimitGuard.ValidateDerivedOutput(10, 10, limits).IsAccepted);
        Assert.Equal(
            RenderLimitReason.OutputByteLimitExceeded,
            RenderLimitGuard.ValidateDerivedOutput(11, 10, limits).Reason);
    }

    [Fact]
    public void ValidateDerivedOutputRequiresLimits()
    {
        Assert.Throws<ArgumentNullException>(() => RenderLimitGuard.ValidateDerivedOutput(1, 1, null!));
    }

    [Fact]
    public void RejectedResultCarriesTheSafeReasonAndNoArtifact()
    {
        var result = RenderLimitResult.Rejected(RenderLimitReason.SourceByteLimitExceeded);

        Assert.True(result.IsRejected);
        Assert.False(result.IsAccepted);
        Assert.Equal(RenderLimitReason.SourceByteLimitExceeded, result.Reason);
        Assert.Equal(RenderLimitReason.None, RenderLimitResult.Accepted.Reason);
        Assert.True(RenderLimitResult.Accepted.IsAccepted);
    }

    [Fact]
    public void RejectedResultRequiresANonNoneReason()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderLimitResult.Rejected(RenderLimitReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderLimitResult.Rejected((RenderLimitReason)999));
    }

    [Fact]
    public void SourceValidationDoesNotMutateTheCallerInput()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        var copy = (byte[])source.Clone();

        _ = RenderLimitGuard.ValidateSourceImage(source.Length, 1000, 1500, DefaultLimits);

        Assert.Equal(copy, source);
    }

    private static int ScalarCount(string value)
    {
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static void AssertWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                Assert.True(index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]));
                index++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(value[index]));
            }
        }
    }
}
