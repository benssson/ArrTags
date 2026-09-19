using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using SkiaSharp;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the real SkiaSharp decode/draw/encode path. These
/// tests are environment-guarded by <see cref="SkiaNativeFactAttribute"/> because
/// the pinned native library needs the transitive <c>libfontconfig.so.1</c> on
/// the loader path; the default suite reports them as skipped, and the pinned
/// runtime command in <c>docs/research/skia-host-compatibility.md</c> runs them.
/// </summary>
public class SkiaBadgeRendererRenderTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [SkiaNativeFact]
    public async Task OpaqueSourceRendersAsRgbPngAtSourceDimensions()
    {
        var source = CreateSource(600, 900, alpha: false);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.True(result.HasArtifact);
        Assert.Equal(RenderResult.PngContentType, result.ContentType);
        Assert.Equal(600, result.Width);
        Assert.Equal(900, result.Height);
        Assert.Matches("^[0-9A-F]{64}$", result.OutputHash!);
        Assert.Matches("^[0-9A-F]{64}$", result.OutputFingerprint!);

        var bytes = result.PngBytes.ToArray();
        Assert.Equal(2, bytes[25]); // RGB
        Assert.Equal(8, bytes[24]); // 8-bit channels
        Assert.Equal(0, bytes[28]); // non-interlaced

        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(600, decoded.Width);
        Assert.Equal(900, decoded.Height);
    }

    [SkiaNativeFact]
    public async Task AlphaSourceRendersAsRgbaPngAndPreservesSourceAlpha()
    {
        var source = CreateSource(600, 900, alpha: true);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var bytes = result.PngBytes.ToArray();
        Assert.Equal(6, bytes[25]); // RGBA

        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);

        var transparent = decoded.GetPixel(5, 5);
        Assert.Equal(0, transparent.Alpha);
        Assert.Equal(new SKColor(0, 0, 0, 0), transparent);

        var opaqueSourcePixel = decoded.GetPixel(300, 500);
        Assert.Equal(255, opaqueSourcePixel.Alpha);
        Assert.Equal(0x20, opaqueSourcePixel.Red);
        Assert.Equal(0x40, opaqueSourcePixel.Green);
        Assert.Equal(0x80, opaqueSourcePixel.Blue);
    }

    [SkiaNativeFact]
    public async Task TechnicalRailAndStatusPillUseTheAdr009Palette()
    {
        var source = CreateSource(600, 900, alpha: false);
        var request = RenderTestFixtures.BuildRequest(
            source,
            metadata: RenderTestFixtures.BuildMetadata(upgradePending: true));

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        using var decoded = SKBitmap.Decode(result.PngBytes.ToArray());
        Assert.NotNull(decoded);

        Assert.True(
            ContainsColor(decoded, 300, 0, 300, 225, new SKColor(0xB4, 0x53, 0x09)),
            "Expected the top-right status pill background.");
        Assert.True(
            ContainsColor(decoded, 0, 675, 300, 225, new SKColor(0x11, 0x18, 0x27)),
            "Expected the bottom-left technical pill background.");
        Assert.False(
            ContainsColor(decoded, 0, 0, 300, 225, new SKColor(0xB4, 0x53, 0x09)),
            "The status pill must not appear in the top-left quadrant.");
    }

    [SkiaNativeFact]
    public async Task SameRequestProducesIdenticalBytesHashAndFingerprint()
    {
        var source = CreateSource(600, 900, alpha: false);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var first = await Renderer.RenderAsync(request, CancellationToken.None);
        var second = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, first.Status);
        Assert.Equal(RenderStatus.Rendered, second.Status);
        Assert.Equal(first.OutputHash, second.OutputHash);
        Assert.Equal(first.OutputFingerprint, second.OutputFingerprint);
        Assert.Equal(first.PngBytes.ToArray(), second.PngBytes.ToArray());
    }

    [SkiaNativeFact]
    public async Task RenderingDoesNotMutateTheSourceBytes()
    {
        var source = CreateSource(600, 900, alpha: false);
        var before = source.Bytes.ToArray();
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(before, source.Bytes.ToArray());
    }

    [SkiaNativeFact]
    public async Task OutputPngDeclaresSrgbAndStripsMetadataChunks()
    {
        var source = CreateSource(600, 900, alpha: true);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var chunks = ReadChunkTypes(result.PngBytes.ToArray());
        Assert.Contains("sRGB", chunks);
        Assert.DoesNotContain("tIME", chunks);
        Assert.DoesNotContain("tEXt", chunks);
        Assert.DoesNotContain("zTXt", chunks);
        Assert.DoesNotContain("iTXt", chunks);
        Assert.DoesNotContain("eXIf", chunks);
    }

    [SkiaNativeFact]
    public async Task OrientationIsAppliedToPixelsBeforeLayout()
    {
        var bytes = CreateOrientedJpeg(600, 900, orientation: 6);
        var source = new SourceImageInput(bytes, "image/jpeg", 900, 600, SourceImageInput.ComputeSha256(bytes));
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(900, result.Width);
        Assert.Equal(600, result.Height);

        using var decoded = SKBitmap.Decode(result.PngBytes.ToArray());
        Assert.NotNull(decoded);
        Assert.Equal(900, decoded.Width);
        Assert.Equal(600, decoded.Height);
    }

    [SkiaNativeFact]
    public async Task DescriptorThatIgnoresOrientationIsRejected()
    {
        var bytes = CreateOrientedJpeg(600, 900, orientation: 6);
        var source = new SourceImageInput(bytes, "image/jpeg", 600, 900, SourceImageInput.ComputeSha256(bytes));
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.MalformedSource, result.FailureReason);
    }

    [SkiaNativeFact]
    public async Task UnsupportedSourceBytesReturnABoundedFailure()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("this is not an image");
        var source = new SourceImageInput(
            bytes,
            "image/png",
            100,
            100,
            SourceImageInput.ComputeSha256(bytes));
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.UnsupportedInput, result.FailureReason);
        Assert.False(result.HasArtifact);
    }

    private static SourceImageInput CreateSource(int width, int height, bool alpha)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb()));

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x40, 0x80, 255));
            if (alpha)
            {
                using var transparent = new SKPaint
                {
                    Color = new SKColor(0, 0, 0, 0),
                    BlendMode = SKBlendMode.Src,
                };
                canvas.DrawRect(new SKRect(0, 0, width, height / 4f), transparent);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        return new SourceImageInput(bytes, "image/png", width, height, SourceImageInput.ComputeSha256(bytes));
    }

    private static System.Collections.Generic.List<string> ReadChunkTypes(byte[] png)
    {
        var chunks = new System.Collections.Generic.List<string>();
        var offset = 8;
        while (offset + 8 <= png.Length)
        {
            var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            chunks.Add(System.Text.Encoding.ASCII.GetString(png, offset + 4, 4));
            offset += 12 + length;
        }

        return chunks;
    }

    private static byte[] CreateOrientedJpeg(int width, int height, ushort orientation)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque,
            SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x40, 0x80));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();

        var app1 = BuildExifApp1(orientation);
        var result = new byte[jpeg.Length + app1.Length];
        result[0] = jpeg[0];
        result[1] = jpeg[1];
        Array.Copy(app1, 0, result, 2, app1.Length);
        Array.Copy(jpeg, 2, result, 2 + app1.Length, jpeg.Length - 2);
        return result;
    }

    private static byte[] BuildExifApp1(ushort orientation)
    {
        // A minimal little-endian TIFF IFD0 containing only the EXIF orientation tag.
        var tiff = new byte[]
        {
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };

        var exifHeader = System.Text.Encoding.ASCII.GetBytes("Exif\0\0");
        var payloadLength = exifHeader.Length + tiff.Length;
        var segmentLength = payloadLength + 2;
        var app1 = new byte[4 + payloadLength];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        app1[2] = (byte)(segmentLength >> 8);
        app1[3] = (byte)(segmentLength & 0xFF);
        Array.Copy(exifHeader, 0, app1, 4, exifHeader.Length);
        Array.Copy(tiff, 0, app1, 4 + exifHeader.Length, tiff.Length);
        return app1;
    }

    private static bool ContainsColor(SKBitmap bitmap, int x, int y, int width, int height, SKColor expected)
    {
        for (var row = y; row < y + height && row < bitmap.Height; row++)
        {
            for (var column = x; column < x + width && column < bitmap.Width; column++)
            {
                var pixel = bitmap.GetPixel(column, row);
                if (pixel.Red == expected.Red && pixel.Green == expected.Green && pixel.Blue == expected.Blue && pixel.Alpha == 255)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
