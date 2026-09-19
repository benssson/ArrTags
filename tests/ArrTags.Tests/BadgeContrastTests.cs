using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the ADR-009 contrast policy: the default palette
/// must pass, a low-contrast or unparseable palette must fail closed with a safe
/// reason, and the WCAG relative-luminance ratio must be computed correctly.
/// </summary>
public class BadgeContrastTests
{
    [Fact]
    public void DefaultPalettePassesContrastValidation()
    {
        Assert.Null(BadgeContrast.Validate(RenderOutputPolicy.Default));
    }

    [Fact]
    public void BlackOnWhiteRatioIsTwentyOne()
    {
        var black = new RgbColor(0, 0, 0);
        var white = new RgbColor(255, 255, 255);

        Assert.Equal(21.0, RgbColor.ContrastRatio(black, white), 3);
        Assert.Equal(21.0, RgbColor.ContrastRatio(white, black), 3);
    }

    [Fact]
    public void LowContrastTechnicalPaletteFailsClosed()
    {
        var policy = new RenderOutputPolicy
        {
            TechnicalBackground = "#111827",
            TechnicalText = "#222222",
        };

        Assert.Equal(RenderFailureReason.ContrastTooLow, BadgeContrast.Validate(policy));
    }

    [Fact]
    public void LowContrastStatusPaletteFailsClosed()
    {
        var policy = new RenderOutputPolicy
        {
            StatusBackground = "#B45309",
            StatusText = "#A05008",
        };

        Assert.Equal(RenderFailureReason.ContrastTooLow, BadgeContrast.Validate(policy));
    }

    [Theory]
    [InlineData("#GGGGGG", "#FFFFFF")]
    [InlineData("#FFFFFF", "not-a-color")]
    [InlineData("", "#FFFFFF")]
    public void UnparseableColorsFailClosed(string technicalBackground, string technicalText)
    {
        var policy = new RenderOutputPolicy
        {
            TechnicalBackground = technicalBackground,
            TechnicalText = technicalText,
        };

        Assert.Equal(RenderFailureReason.ColorPolicyInvalid, BadgeContrast.Validate(policy));
    }

    [Theory]
    [InlineData("#111827", true)]
    [InlineData("111827", true)]
    [InlineData("#FFF", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ColorParsingAcceptsOnlySixDigitHex(string? text, bool expected)
    {
        Assert.Equal(expected, RgbColor.TryParse(text, out _));
    }
}
