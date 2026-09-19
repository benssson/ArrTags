using System;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the pure orientation geometry: the four
/// axis-aligned orientations keep the dimensions and the four transposing
/// orientations swap them. Pixel-level orientation is covered by the guarded
/// render and the later golden tests.
/// </summary>
public class SourceOrientationTests
{
    [Theory]
    [InlineData(SourceOrientation.None)]
    [InlineData(SourceOrientation.TopLeft)]
    [InlineData(SourceOrientation.TopRight)]
    [InlineData(SourceOrientation.BottomRight)]
    [InlineData(SourceOrientation.BottomLeft)]
    public void AxisAlignedOrientationsPreserveDimensions(SourceOrientation orientation)
    {
        Assert.False(orientation.SwapsAxes());
        Assert.Equal((1000, 1500), orientation.OrientedDimensions(1000, 1500));
    }

    [Theory]
    [InlineData(SourceOrientation.LeftTop)]
    [InlineData(SourceOrientation.RightTop)]
    [InlineData(SourceOrientation.RightBottom)]
    [InlineData(SourceOrientation.LeftBottom)]
    public void TransposingOrientationsSwapDimensions(SourceOrientation orientation)
    {
        Assert.True(orientation.SwapsAxes());
        Assert.Equal((1500, 1000), orientation.OrientedDimensions(1000, 1500));
    }

    [Fact]
    public void UndefinedOrientationIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ((SourceOrientation)99).SwapsAxes());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ((SourceOrientation)99).OrientedDimensions(10, 10));
    }

    [Fact]
    public void NonPositiveDimensionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SourceOrientation.TopLeft.OrientedDimensions(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SourceOrientation.TopLeft.OrientedDimensions(10, -1));
    }
}
