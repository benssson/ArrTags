using System;

namespace ArrTags.Rendering;

/// <summary>
/// Pure orientation geometry helpers. They compute the oriented output
/// dimensions without touching image pixels, so the dimension-swap policy is
/// testable without a raster library.
/// </summary>
public static class SourceOrientationExtensions
{
    /// <summary>
    /// Determines whether an orientation swaps the image axes.
    /// </summary>
    /// <param name="orientation">The source orientation.</param>
    /// <returns><see langword="true"/> when width and height are exchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The orientation is not defined.</exception>
    public static bool SwapsAxes(this SourceOrientation orientation)
    {
        return orientation switch
        {
            SourceOrientation.None => false,
            SourceOrientation.TopLeft => false,
            SourceOrientation.TopRight => false,
            SourceOrientation.BottomRight => false,
            SourceOrientation.BottomLeft => false,
            SourceOrientation.LeftTop => true,
            SourceOrientation.RightTop => true,
            SourceOrientation.RightBottom => true,
            SourceOrientation.LeftBottom => true,
            _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown source orientation."),
        };
    }

    /// <summary>
    /// Computes the oriented dimensions after applying an orientation.
    /// </summary>
    /// <param name="orientation">The source orientation.</param>
    /// <param name="width">The encoded width in pixels.</param>
    /// <param name="height">The encoded height in pixels.</param>
    /// <returns>The oriented width and height in pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The orientation is not defined or a dimension is not positive.</exception>
    public static (int Width, int Height) OrientedDimensions(
        this SourceOrientation orientation,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return orientation.SwapsAxes()
            ? (height, width)
            : (width, height);
    }
}
