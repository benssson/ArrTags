using System;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 unit tests for the ADR-010 cross-runtime tolerance
/// comparator. These use synthetic packed pixel buffers and do not load the
/// native renderer, so they run unguarded in the default suite. They cover the
/// exact canonical rule, the tolerant non-canonical rule, the 0.1 percent
/// boundary, alpha exactness, and length/dimension validation.
/// </summary>
public class CrossRuntimePixelComparatorTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ExactModeAcceptsIdenticalPlanes(int channels)
    {
        var expected = CreatePlane(20, 20, channels, 0x40);
        var actual = CreatePlane(20, 20, channels, 0x40);

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            20,
            20,
            channels,
            PixelComparisonMode.Exact);

        Assert.True(result.IsMatch);
        Assert.Equal(0, result.DifferingPixels);
    }

    [Fact]
    public void ExactModeRejectsAnyChannelDifference()
    {
        var expected = CreatePlane(20, 20, 3, 0x40);
        var actual = CreatePlane(20, 20, 3, 0x40);
        actual[5] = 0x41;

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            20,
            20,
            3,
            PixelComparisonMode.Exact);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ExactModeRejectsAlphaDifference()
    {
        var expected = CreatePlane(20, 20, 4, 0x40);
        var actual = CreatePlane(20, 20, 4, 0x40);
        actual[3] = 0x80;

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            20,
            20,
            4,
            PixelComparisonMode.Exact);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void TolerantModeAcceptsSingleStepDifferenceWithinBudget()
    {
        // 1000 pixels -> allowed budget is one pixel.
        var expected = CreatePlane(100, 10, 3, 0x40);
        var actual = CreatePlane(100, 10, 3, 0x40);
        actual[0] = 0x41;

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            100,
            10,
            3,
            PixelComparisonMode.TolerantAntiAliasedText);

        Assert.True(result.IsMatch);
        Assert.Equal(1, result.DifferingPixels);
        Assert.Equal(1, result.AllowedDifferingPixels);
    }

    [Fact]
    public void TolerantModeAcceptsExactlyTheBudgetBoundary()
    {
        // 2000 pixels -> allowed budget is two pixels.
        var expected = CreatePlane(200, 10, 3, 0x40);
        var actual = CreatePlane(200, 10, 3, 0x40);
        actual[0] = 0x41;
        actual[3] = 0x3F;

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            200,
            10,
            3,
            PixelComparisonMode.TolerantAntiAliasedText);

        Assert.True(result.IsMatch);
        Assert.Equal(2, result.DifferingPixels);
        Assert.Equal(2, result.AllowedDifferingPixels);
    }

    [Fact]
    public void TolerantModeRejectsOnePixelBeyondTheBudget()
    {
        // 1000 pixels -> allowed budget is one pixel, so two differ.
        var expected = CreatePlane(100, 10, 3, 0x40);
        var actual = CreatePlane(100, 10, 3, 0x40);
        actual[0] = 0x41;
        actual[3] = 0x41;

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            100,
            10,
            3,
            PixelComparisonMode.TolerantAntiAliasedText);

        Assert.False(result.IsMatch);
        Assert.Equal(2, result.DifferingPixels);
        Assert.Equal(1, result.AllowedDifferingPixels);
    }

    [Fact]
    public void TolerantModeRejectsDifferenceGreaterThanOne()
    {
        var expected = CreatePlane(100, 10, 3, 0x40);
        var actual = CreatePlane(100, 10, 3, 0x40);
        actual[0] = 0x42;

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            100,
            10,
            3,
            PixelComparisonMode.TolerantAntiAliasedText);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void TolerantModeRejectsAlphaDifference()
    {
        var expected = CreatePlane(100, 10, 4, 0x40);
        var actual = CreatePlane(100, 10, 4, 0x40);
        actual[3] = 0x7F;

        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            100,
            10,
            4,
            PixelComparisonMode.TolerantAntiAliasedText);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void TolerantModeRejectsALargeSolidRegionMove()
    {
        // A solid region next to a different solid region: one edge pixel moves
        // by one, which stays within the budget, while the interiors are equal.
        var expected = CreatePlane(100, 10, 3, 0x40);
        var actual = CreatePlane(100, 10, 3, 0x40);
        for (var x = 50; x < 100; x++)
        {
            for (var y = 0; y < 10; y++)
            {
                var offset = ((y * 100) + x) * 3;
                actual[offset] = 0x60;
                actual[offset + 1] = 0x60;
                actual[offset + 2] = 0x60;
            }
        }

        // The whole right half now differs by more than one step.
        var result = CrossRuntimePixelComparator.Compare(
            expected,
            actual,
            100,
            10,
            3,
            PixelComparisonMode.TolerantAntiAliasedText);

        Assert.False(result.IsMatch);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void InvalidDimensionsAreRejected(int width, int height)
    {
        var plane = CreatePlane(10, 10, 3, 0x40);

        var result = CrossRuntimePixelComparator.Compare(
            plane.AsSpan(),
            plane.AsSpan(),
            width,
            height,
            3,
            PixelComparisonMode.Exact);

        Assert.False(result.IsMatch);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void InvalidChannelCountIsRejected(int channels)
    {
        var plane = CreatePlane(10, 10, 3, 0x40);

        var result = CrossRuntimePixelComparator.Compare(
            plane.AsSpan(),
            plane.AsSpan(),
            10,
            10,
            channels,
            PixelComparisonMode.Exact);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void LengthMismatchIsRejected()
    {
        var expected = CreatePlane(10, 10, 3, 0x40);
        var actual = CreatePlane(10, 10, 3, 0x40);
        Array.Resize(ref actual, actual.Length - 3);

        var result = CrossRuntimePixelComparator.Compare(
            expected.AsSpan(),
            actual.AsSpan(),
            10,
            10,
            3,
            PixelComparisonMode.Exact);

        Assert.False(result.IsMatch);
    }

    private static byte[] CreatePlane(int width, int height, int channels, byte value)
    {
        var plane = new byte[width * height * channels];
        for (var index = 0; index < plane.Length; index++)
        {
            plane[index] = value;
        }

        if (channels == 4)
        {
            for (var offset = 3; offset < plane.Length; offset += 4)
            {
                plane[offset] = 0xFF;
            }
        }

        return plane;
    }
}
