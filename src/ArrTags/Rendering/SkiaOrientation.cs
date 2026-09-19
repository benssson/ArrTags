using System;
using SkiaSharp;

namespace ArrTags.Rendering;

/// <summary>
/// Applies a decoded source's EXIF orientation to its pixels before layout. This
/// is an internal SkiaSharp implementation detail: no SkiaSharp type crosses the
/// renderer boundary.
/// </summary>
internal static class SkiaOrientation
{
    /// <summary>
    /// Maps a SkiaSharp encoded origin to the provider-neutral orientation.
    /// </summary>
    /// <param name="origin">The encoded origin reported by the codec.</param>
    /// <returns>The provider-neutral orientation.</returns>
    public static SourceOrientation FromEncodedOrigin(SKEncodedOrigin origin)
    {
        return origin switch
        {
            SKEncodedOrigin.TopRight => SourceOrientation.TopRight,
            SKEncodedOrigin.BottomRight => SourceOrientation.BottomRight,
            SKEncodedOrigin.BottomLeft => SourceOrientation.BottomLeft,
            SKEncodedOrigin.LeftTop => SourceOrientation.LeftTop,
            SKEncodedOrigin.RightTop => SourceOrientation.RightTop,
            SKEncodedOrigin.RightBottom => SourceOrientation.RightBottom,
            SKEncodedOrigin.LeftBottom => SourceOrientation.LeftBottom,
            _ => SourceOrientation.TopLeft,
        };
    }

    /// <summary>
    /// Applies an orientation to a decoded bitmap and returns a new bitmap with
    /// the oriented dimensions. The source bitmap is never mutated.
    /// </summary>
    /// <param name="source">The decoded source bitmap.</param>
    /// <param name="orientation">The orientation to apply.</param>
    /// <returns>A new oriented bitmap.</returns>
    public static SKBitmap Apply(SKBitmap source, SourceOrientation orientation)
    {
        var (width, height) = orientation.OrientedDimensions(source.Width, source.Height);
        var output = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType, source.ColorSpace));

        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        switch (orientation)
        {
            case SourceOrientation.None:
            case SourceOrientation.TopLeft:
                break;
            case SourceOrientation.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SourceOrientation.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SourceOrientation.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SourceOrientation.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SourceOrientation.RightTop:
                canvas.Translate(height, 0);
                canvas.RotateDegrees(90);
                break;
            case SourceOrientation.RightBottom:
                canvas.Translate(height, width);
                canvas.RotateDegrees(270);
                canvas.Scale(1, -1);
                break;
            case SourceOrientation.LeftBottom:
                canvas.Translate(0, width);
                canvas.RotateDegrees(270);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown source orientation.");
        }

        canvas.DrawBitmap(source, 0, 0);
        canvas.Restore();
        return output;
    }
}
