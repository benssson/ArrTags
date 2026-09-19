using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using SkiaSharp;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 content-coverage tests for all eight EXIF orientations. An
/// opaque source must never gain transparency or lose content when orientation is
/// applied, and each rotation/mirror must move the two corner markers to the
/// expected quadrant. These tests load the native renderer and are
/// environment-guarded.
/// </summary>
public class RenderOrientationTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    /// <summary>
    /// Gets the orientation cases: the EXIF value plus the expected red
    /// (source top-left) and green (source top-right) marker quadrants.
    /// </summary>
    public static IEnumerable<object[]> Orientations =>
        new[]
        {
            new object[] { (ushort)1, "TopLeft", "TopRight" },
            new object[] { (ushort)2, "TopRight", "TopLeft" },
            new object[] { (ushort)3, "BottomRight", "BottomLeft" },
            new object[] { (ushort)4, "BottomLeft", "BottomRight" },
            new object[] { (ushort)5, "TopLeft", "BottomLeft" },
            new object[] { (ushort)6, "TopRight", "BottomRight" },
            new object[] { (ushort)7, "BottomRight", "TopRight" },
            new object[] { (ushort)8, "BottomLeft", "TopLeft" },
        };

    [SkiaNativeTheory]
    [MemberData(nameof(Orientations))]
    public async Task EveryOrientationIsFullyOpaqueAndPlacesBothMarkersCorrectly(
        ushort orientation,
        string redQuadrant,
        string greenQuadrant)
    {
        var swapsAxes = orientation >= 5;
        var expectedWidth = swapsAxes ? RenderGoldenFixtures.OrientedWidth : RenderGoldenFixtures.Width;
        var expectedHeight = swapsAxes ? RenderGoldenFixtures.OrientedHeight : RenderGoldenFixtures.Height;

        var bytes = RenderImageFixtures.CreateTwoMarkerOrientedJpeg(
            RenderGoldenFixtures.Width,
            RenderGoldenFixtures.Height,
            orientation);
        var source = RenderImageFixtures.JpegSource(bytes, expectedWidth, expectedHeight);

        // A single technical value keeps the bottom-left rail small so it cannot
        // cover a marker's centroid.
        var metadata = RenderImageFixtures.BuildMetadata(
            quality: null,
            resolution: null,
            videoCodec: null,
            source: "WEB-DL");
        var request = RenderTestFixtures.BuildRequest(source, metadata: metadata);

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);

        using var decoded = SKBitmap.Decode(result.PngBytes.ToArray());
        Assert.NotNull(decoded);
        Assert.Equal(expectedWidth, decoded.Width);
        Assert.Equal(expectedHeight, decoded.Height);

        var transparent = 0;
        var redPixels = new List<(int X, int Y)>();
        var greenPixels = new List<(int X, int Y)>();
        for (var y = 0; y < decoded.Height; y++)
        {
            for (var x = 0; x < decoded.Width; x++)
            {
                var color = decoded.GetPixel(x, y);
                if (color.Alpha != 255)
                {
                    transparent++;
                }

                if (IsRedMarker(color))
                {
                    redPixels.Add((x, y));
                }
                else if (IsGreenMarker(color))
                {
                    greenPixels.Add((x, y));
                }
            }
        }

        Assert.Equal(0, transparent);
        Assert.True(redPixels.Count > 0, "The red corner marker was not found in the output.");
        Assert.True(greenPixels.Count > 0, "The green corner marker was not found in the output.");
        Assert.Equal(redQuadrant, Quadrant(Centroid(redPixels), decoded.Width, decoded.Height));
        Assert.Equal(greenQuadrant, Quadrant(Centroid(greenPixels), decoded.Width, decoded.Height));
    }

    private static bool IsRedMarker(SKColor color)
    {
        return color.Alpha == 255
            && color.Red >= 0xA0
            && color.Green <= 0x60
            && color.Blue <= 0x60;
    }

    private static bool IsGreenMarker(SKColor color)
    {
        return color.Alpha == 255
            && color.Green >= 0x80
            && color.Red <= 0x60
            && color.Blue <= 0x60;
    }

    private static (double X, double Y) Centroid(List<(int X, int Y)> pixels)
    {
        double sumX = 0;
        double sumY = 0;
        foreach (var (x, y) in pixels)
        {
            sumX += x;
            sumY += y;
        }

        return (sumX / pixels.Count, sumY / pixels.Count);
    }

    private static string Quadrant((double X, double Y) centroid, int width, int height)
    {
        var vertical = centroid.Y < height / 2.0 ? "Top" : "Bottom";
        var horizontal = centroid.X < width / 2.0 ? "Left" : "Right";
        return vertical + horizontal;
    }
}
