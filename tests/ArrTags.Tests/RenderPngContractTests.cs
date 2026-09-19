using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using SkiaSharp;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 PNG-contract tests. They assert the ADR-010 output
/// structure (non-interlaced 8-bit RGB/RGBA, fixed sRGB declaration, stripped
/// source metadata, canonical transparent-pixel RGB, source-alpha preservation)
/// and the source color-profile contract: an invalid or unsupported embedded
/// profile fails closed with a bounded reason, while a supported profile is
/// accepted and converted to sRGB. These tests load the native renderer and are
/// environment-guarded.
/// </summary>
public class RenderPngContractTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [SkiaNativeFact]
    public async Task OpaqueOutputIsNonInterlacedEightBitRgbWithFixedSrgbAndNoMetadata()
    {
        var source = RenderImageFixtures.CreateRgbPng(RenderGoldenFixtures.Width, RenderGoldenFixtures.Height);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderImageFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var png = result.PngBytes.ToArray();
        AssertPngStructure(png, RenderGoldenFixtures.Width, RenderGoldenFixtures.Height, colorType: 2);

        var chunks = RendererBehaviorFixtures.ReadChunkTypes(png);
        Assert.Contains("sRGB", chunks);
        Assert.Equal("IEND", chunks[^1]);
        AssertNoStrippedMetadata(chunks);
    }

    [SkiaNativeFact]
    public async Task AlphaOutputIsNonInterlacedEightBitRgbaAndPreservesStraightSourceAlpha()
    {
        var source = RendererBehaviorFixtures.CreateSemiTransparentPng(
            RenderGoldenFixtures.Width,
            RenderGoldenFixtures.Height);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderImageFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var png = result.PngBytes.ToArray();
        AssertPngStructure(png, RenderGoldenFixtures.Width, RenderGoldenFixtures.Height, colorType: 6);
        Assert.Contains("sRGB", RendererBehaviorFixtures.ReadChunkTypes(png));
        AssertNoStrippedMetadata(RendererBehaviorFixtures.ReadChunkTypes(png));

        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        var pixel = decoded.GetPixel(
            RendererBehaviorFixtures.SemiTransparentSampleX,
            RendererBehaviorFixtures.SemiTransparentSampleY);
        Assert.Equal(RendererBehaviorFixtures.SemiTransparentAlpha, pixel.Alpha);
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
    public async Task FullyTransparentSourcePixelsUseCanonicalZeroRgb()
    {
        var bytes = RendererBehaviorFixtures.BuildHiddenRgbPng(400, 600, hiddenX: 1, hiddenY: 1);
        var source = RenderImageFixtures.Source(bytes, 400, 600);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderImageFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        using var decoded = SKBitmap.Decode(result.PngBytes.ToArray());
        Assert.NotNull(decoded);
        Assert.Equal(new SKColor(0, 0, 0, 0), decoded.GetPixel(1, 1));
    }

    [SkiaNativeFact]
    public async Task SourceContainerMetadataIsStrippedFromOutput()
    {
        var source = RenderImageFixtures.CreateMetadataBearingPng(
            RenderGoldenFixtures.Width,
            RenderGoldenFixtures.Height);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderImageFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var chunks = RendererBehaviorFixtures.ReadChunkTypes(result.PngBytes.ToArray());
        Assert.Contains("sRGB", chunks);
        AssertNoStrippedMetadata(chunks);
    }

    [SkiaNativeFact]
    public async Task MalformedEmbeddedIccProfileFailsClosed()
    {
        var bytes = RenderImageFixtures.InsertPngIccp(
            RenderImageFixtures.CreateRgbPng(RenderGoldenFixtures.Width, RenderGoldenFixtures.Height).Bytes.ToArray(),
            Encoding.ASCII.GetBytes("not an icc profile at all"));
        await AssertProfileRejectedAsync(bytes);
    }

    [SkiaNativeFact]
    public async Task UnsupportedEmbeddedIccProfileFailsClosed()
    {
        var garbage = new byte[512];
        new Random(20260919).NextBytes(garbage);

        var bytes = RenderImageFixtures.InsertPngIccp(
            RenderImageFixtures.CreateRgbPng(RenderGoldenFixtures.Width, RenderGoldenFixtures.Height).Bytes.ToArray(),
            garbage);
        await AssertProfileRejectedAsync(bytes);
    }

    [SkiaNativeFact]
    public async Task SupportedEmbeddedIccProfileIsAcceptedAndConverted()
    {
        var bytes = RenderImageFixtures.InsertPngIccp(
            RenderImageFixtures.CreateRgbPng(RenderGoldenFixtures.Width, RenderGoldenFixtures.Height).Bytes.ToArray(),
            RenderImageFixtures.BuildSrgbLikeIcc());
        var source = RenderImageFixtures.Source(bytes, RenderGoldenFixtures.Width, RenderGoldenFixtures.Height);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderImageFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var png = result.PngBytes.ToArray();
        Assert.Contains("sRGB", RendererBehaviorFixtures.ReadChunkTypes(png));
        Assert.DoesNotContain("iCCP", RendererBehaviorFixtures.ReadChunkTypes(png));

        using var decoded = SKBitmap.Decode(png);
        Assert.NotNull(decoded);
        var pixel = decoded.GetPixel(5, 5);
        Assert.NotEqual(
            new SKColor(RenderImageFixtures.BackgroundRed, RenderImageFixtures.BackgroundGreen, RenderImageFixtures.BackgroundBlue),
            new SKColor(pixel.Red, pixel.Green, pixel.Blue));
    }

    private static async Task AssertProfileRejectedAsync(byte[] bytes)
    {
        var source = RenderImageFixtures.Source(bytes, RenderGoldenFixtures.Width, RenderGoldenFixtures.Height);
        var before = source.Bytes.ToArray();
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderImageFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.UnsupportedColorProfile, result.FailureReason);
        Assert.False(result.HasArtifact);
        Assert.True(result.PngBytes.IsEmpty);
        Assert.Null(result.PassThroughReason);
        Assert.Null(result.OutputHash);
        Assert.Null(result.OutputFingerprint);
        Assert.Equal(before, source.Bytes.ToArray());
    }

    private static void AssertPngStructure(byte[] png, int width, int height, int colorType)
    {
        Assert.True(png.Length > 33);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(13, RendererBehaviorFixtures.ReadBigEndianInt32(png, 8));
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(width, RendererBehaviorFixtures.ReadBigEndianInt32(png, 16));
        Assert.Equal(height, RendererBehaviorFixtures.ReadBigEndianInt32(png, 20));
        Assert.Equal(8, png[24]);
        Assert.Equal(colorType, png[25]);
        Assert.Equal(0, png[26]);
        Assert.Equal(0, png[27]);
        Assert.Equal(0, png[28]);
    }

    private static void AssertNoStrippedMetadata(List<string> chunks)
    {
        Assert.DoesNotContain("iCCP", chunks);
        Assert.DoesNotContain("eXIf", chunks);
        Assert.DoesNotContain("tIME", chunks);
        Assert.DoesNotContain("tEXt", chunks);
        Assert.DoesNotContain("zTXt", chunks);
        Assert.DoesNotContain("iTXt", chunks);
    }
}
