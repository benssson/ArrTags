using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using SkiaSharp;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.6 format behavior. The behavioral contract is a non-interlaced
/// 8-bit sRGB PNG that is RGB for fully opaque output and RGBA when the source
/// has meaningful alpha, preserves straight source alpha, canonicalizes fully
/// transparent pixels, and contains no copied source EXIF/ICC/text/timestamp
/// metadata. These are decoded/structural behavior assertions, not the pinned
/// golden or strict byte oracle owned by task 4.11.
/// </summary>
public class RendererBehaviorFormatTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [SkiaNativeFact]
    public async Task OpaqueOutputIsAnEightBitNonInterlacedRgbPng()
    {
        var source = RendererBehaviorFixtures.CreateOpaquePng(600, 900);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(RenderResult.PngContentType, result.ContentType);

        var png = result.PngBytes.ToArray();
        AssertPngSignature(png);
        Assert.Equal(13, RendererBehaviorFixtures.ReadBigEndianInt32(png, 8));
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal((600, 900), RendererBehaviorFixtures.ReadIhdrDimensions(png));
        Assert.Equal(8, png[24]);
        Assert.Equal(2, png[25]);
        Assert.Equal(0, png[26]);
        Assert.Equal(0, png[27]);
        Assert.Equal(0, png[28]);

        var chunks = RendererBehaviorFixtures.ReadChunkTypes(png);
        Assert.Contains("IDAT", chunks);
        Assert.Contains("sRGB", chunks);
        Assert.Equal("IEND", chunks[^1]);
        AssertNoRetainedSourceMetadata(chunks);

        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(255, decoded.GetPixel(5, 5).Alpha);
    }

    [SkiaNativeFact]
    public async Task MeaningfulAlphaOutputIsAnEightBitNonInterlacedRgbaPng()
    {
        var source = RendererBehaviorFixtures.CreateAlphaPng(600, 900);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var png = result.PngBytes.ToArray();
        AssertPngSignature(png);
        Assert.Equal((600, 900), RendererBehaviorFixtures.ReadIhdrDimensions(png));
        Assert.Equal(8, png[24]);
        Assert.Equal(6, png[25]);
        Assert.Equal(0, png[26]);
        Assert.Equal(0, png[27]);
        Assert.Equal(0, png[28]);

        var chunks = RendererBehaviorFixtures.ReadChunkTypes(png);
        Assert.Contains("IDAT", chunks);
        Assert.Contains("sRGB", chunks);
        Assert.Equal("IEND", chunks[^1]);
        AssertNoRetainedSourceMetadata(chunks);
    }

    [SkiaNativeFact]
    public async Task SemiTransparentSourceAlphaIsPreservedAsStraightAlpha()
    {
        var source = RendererBehaviorFixtures.CreateSemiTransparentPng(600, 900);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        using var decoded = SKBitmap.Decode(result.PngBytes.ToArray());
        Assert.NotNull(decoded);

        var pixel = decoded.GetPixel(
            RendererBehaviorFixtures.SemiTransparentSampleX,
            RendererBehaviorFixtures.SemiTransparentSampleY);
        Assert.Equal(RendererBehaviorFixtures.SemiTransparentAlpha, pixel.Alpha);

        // Straight alpha: the RGB must round-trip to the source color within one
        // channel step, not the roughly halved premultiplied value.
        Assert.InRange(
            pixel.Red,
            RendererBehaviorFixtures.SemiTransparentRed - 1,
            RendererBehaviorFixtures.SemiTransparentRed + 1);
        Assert.InRange(
            pixel.Green,
            RendererBehaviorFixtures.SemiTransparentGreen - 1,
            RendererBehaviorFixtures.SemiTransparentGreen + 1);
        Assert.InRange(
            pixel.Blue,
            RendererBehaviorFixtures.SemiTransparentBlue - 1,
            RendererBehaviorFixtures.SemiTransparentBlue + 1);
    }

    [SkiaNativeFact]
    public async Task FullyTransparentSourcePixelsAreCanonicalizedToZeroRgb()
    {
        // The source carries non-zero hidden RGB on a fully transparent pixel, so
        // the output must canonicalize it rather than copy the hidden bytes.
        var bytes = RendererBehaviorFixtures.BuildHiddenRgbPng(400, 600, hiddenX: 1, hiddenY: 1);
        var source = RendererBehaviorFixtures.Source(bytes, 400, 600);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        using var decoded = SKBitmap.Decode(result.PngBytes.ToArray());
        Assert.NotNull(decoded);
        Assert.Equal(new SKColor(0, 0, 0, 0), decoded.GetPixel(1, 1));
    }

    private static void AssertPngSignature(byte[] png)
    {
        Assert.True(png.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
    }

    private static void AssertNoRetainedSourceMetadata(System.Collections.Generic.List<string> chunks)
    {
        Assert.DoesNotContain("iCCP", chunks);
        Assert.DoesNotContain("eXIf", chunks);
        Assert.DoesNotContain("tIME", chunks);
        Assert.DoesNotContain("tEXt", chunks);
        Assert.DoesNotContain("zTXt", chunks);
        Assert.DoesNotContain("iTXt", chunks);
    }
}
