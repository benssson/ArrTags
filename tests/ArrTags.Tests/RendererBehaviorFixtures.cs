using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Rendering;
using SkiaSharp;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Shared helpers for the Phase 4 task 4.6 renderer behavior matrix: synthetic
/// repository-owned source fixtures, metadata builders, and PNG structural
/// parsing. The Skia-backed image builders are only called from
/// environment-guarded tests; the pure helpers are usable unguarded. No helper
/// reads an external image or a copyrighted poster.
/// </summary>
internal static class RendererBehaviorFixtures
{
    /// <summary>The semi-transparent fixture red channel.</summary>
    public const int SemiTransparentRed = 0x11;

    /// <summary>The semi-transparent fixture green channel.</summary>
    public const int SemiTransparentGreen = 0x22;

    /// <summary>The semi-transparent fixture blue channel.</summary>
    public const int SemiTransparentBlue = 0x33;

    /// <summary>The semi-transparent fixture alpha channel.</summary>
    public const int SemiTransparentAlpha = 128;

    /// <summary>The sampled semi-transparent fixture column.</summary>
    public const int SemiTransparentSampleX = 5;

    /// <summary>The sampled semi-transparent fixture row.</summary>
    public const int SemiTransparentSampleY = 5;

    /// <summary>
    /// Builds a validated source descriptor over the supplied bytes.
    /// </summary>
    /// <param name="bytes">The exact source bytes.</param>
    /// <param name="width">The oriented source width.</param>
    /// <param name="height">The oriented source height.</param>
    /// <returns>A source descriptor with the verified content hash.</returns>
    public static SourceImageInput Source(byte[] bytes, int width, int height)
    {
        return new SourceImageInput(bytes, "image/png", width, height, SourceImageInput.ComputeSha256(bytes));
    }

    /// <summary>
    /// Builds canonical metadata with optional custom values and an upgrade flag.
    /// </summary>
    /// <param name="customBadges">The ordered custom badge values, when any.</param>
    /// <param name="upgradePending">The tri-state upgrade-pending value.</param>
    /// <returns>Canonical metadata for a matched Radarr movie.</returns>
    public static BadgeMetadata BuildMetadata(
        IEnumerable<string>? customBadges = null,
        bool? upgradePending = null)
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
        var recordIdentity = new RadarrIdentity(connectionId, 7, ArrFileIdentity.Present(42));

        return new BadgeMetadata(
            provider,
            recordIdentity,
            RenderTestFixtures.ObservedAt,
            quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "none", 7),
            resolution: new ArrResolutionDescriptor(1920, 1080, "1080p", ArrMetadataOrigin.ProviderMediaInfo),
            videoCodec: "x265",
            source: "WEB-DL",
            customBadges: customBadges,
            upgradePending: upgradePending);
    }

    /// <summary>
    /// Creates an opaque sRGB PNG source of the requested dimensions.
    /// </summary>
    /// <param name="width">The source width.</param>
    /// <param name="height">The source height.</param>
    /// <returns>The encoded source descriptor.</returns>
    public static SourceImageInput CreateOpaquePng(int width, int height)
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
        }

        return EncodePng(bitmap, width, height);
    }

    /// <summary>
    /// Creates an sRGB PNG source with a fully transparent top region so the
    /// decoded image has meaningful alpha.
    /// </summary>
    /// <param name="width">The source width.</param>
    /// <param name="height">The source height.</param>
    /// <returns>The encoded source descriptor.</returns>
    public static SourceImageInput CreateAlphaPng(int width, int height)
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
            using var transparent = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 0),
                BlendMode = SKBlendMode.Src,
            };
            canvas.DrawRect(new SKRect(0, 0, width, height / 4f), transparent);
        }

        return EncodePng(bitmap, width, height);
    }

    /// <summary>
    /// Creates an sRGB PNG source containing a semi-transparent region so
    /// straight-alpha preservation can be observed after a render.
    /// </summary>
    /// <param name="width">The source width.</param>
    /// <param name="height">The source height.</param>
    /// <returns>The encoded source descriptor.</returns>
    public static SourceImageInput CreateSemiTransparentPng(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul,
            SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x40, 0x80, 255));
            using var paint = new SKPaint
            {
                Color = new SKColor(
                    SemiTransparentRed,
                    SemiTransparentGreen,
                    SemiTransparentBlue,
                    SemiTransparentAlpha),
                BlendMode = SKBlendMode.Src,
            };
            canvas.DrawRect(new SKRect(0, 0, width, height / 4f), paint);
        }

        return EncodePng(bitmap, width, height);
    }

    /// <summary>
    /// Hand-crafts a straight-alpha RGBA PNG whose single requested pixel is fully
    /// transparent but carries non-zero hidden RGB. Encoding through Skia
    /// canonicalizes that hidden value, so the bytes must be built directly to
    /// verify the renderer's canonical transparent-pixel policy.
    /// </summary>
    /// <param name="width">The source width; must be greater than one.</param>
    /// <param name="height">The source height; must be greater than one.</param>
    /// <param name="hiddenX">The hidden pixel column.</param>
    /// <param name="hiddenY">The hidden pixel row.</param>
    /// <returns>Valid PNG bytes with the hidden transparent pixel.</returns>
    public static byte[] BuildHiddenRgbPng(int width, int height, int hiddenX, int hiddenY)
    {
        var stride = (width * 4) + 1;
        var raw = new byte[height * stride];
        for (var row = 0; row < height; row++)
        {
            var rowStart = row * stride;
            raw[rowStart] = 0;
            for (var column = 0; column < width; column++)
            {
                var pixel = rowStart + 1 + (column * 4);
                if (column == hiddenX && row == hiddenY)
                {
                    raw[pixel] = 0xAB;
                    raw[pixel + 1] = 0xCD;
                    raw[pixel + 2] = 0xEF;
                    raw[pixel + 3] = 0x00;
                }
                else
                {
                    raw[pixel] = 0x20;
                    raw[pixel + 1] = 0x40;
                    raw[pixel + 2] = 0x80;
                    raw[pixel + 3] = 0xFF;
                }
            }
        }

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw, 0, raw.Length);
            }

            compressed = output.ToArray();
        }

        var header = new byte[13];
        WriteBigEndianInt32(header, 0, width);
        WriteBigEndianInt32(header, 4, height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;

        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        WritePngChunk(png, "IHDR", header);
        WritePngChunk(png, "IDAT", compressed);
        WritePngChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    /// <summary>
    /// Encodes a synthetic JPEG of the requested dimensions and inserts a minimal
    /// EXIF APP1 segment carrying only the requested orientation tag.
    /// </summary>
    /// <param name="width">The encoded width.</param>
    /// <param name="height">The encoded height.</param>
    /// <param name="orientation">The EXIF orientation value.</param>
    /// <returns>The JPEG bytes with the EXIF orientation tag.</returns>
    public static byte[] CreateOrientedJpeg(int width, int height, ushort orientation)
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

    /// <summary>
    /// Reads the ordered PNG chunk type names from a complete PNG.
    /// </summary>
    /// <param name="png">The PNG bytes.</param>
    /// <returns>The chunk type names in file order.</returns>
    public static List<string> ReadChunkTypes(byte[] png)
    {
        var chunks = new List<string>();
        var offset = 8;
        while (offset + 8 <= png.Length)
        {
            var length = ReadBigEndianInt32(png, offset);
            chunks.Add(Encoding.ASCII.GetString(png, offset + 4, 4));
            offset += 12 + length;
        }

        return chunks;
    }

    /// <summary>
    /// Reads the IHDR width and height.
    /// </summary>
    /// <param name="png">A PNG whose first chunk is a valid IHDR.</param>
    /// <returns>The encoded width and height.</returns>
    public static (int Width, int Height) ReadIhdrDimensions(byte[] png)
    {
        return (ReadBigEndianInt32(png, 16), ReadBigEndianInt32(png, 20));
    }

    /// <summary>
    /// Reads a big-endian signed 32-bit integer from a PNG field.
    /// </summary>
    /// <param name="bytes">The source bytes.</param>
    /// <param name="offset">The field offset.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    /// <summary>
    /// Counts Unicode scalar values, not UTF-16 code units.
    /// </summary>
    /// <param name="value">The text to count.</param>
    /// <returns>The scalar count.</returns>
    public static int ScalarCount(string value)
    {
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            _ = rune;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Asserts that the text contains no lone surrogate code unit.
    /// </summary>
    /// <param name="value">The text to inspect.</param>
    public static void AssertWellFormedUtf16(string value)
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

    private static SourceImageInput EncodePng(SKBitmap bitmap, int width, int height)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Source(data.ToArray(), width, height);
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

        var exifHeader = Encoding.ASCII.GetBytes("Exif\0\0");
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

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndianInt32(length, 0, data.Length);
        stream.Write(length, 0, 4);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, 4);
        stream.Write(data, 0, data.Length);

        var crcInput = new byte[4 + data.Length];
        Array.Copy(typeBytes, 0, crcInput, 0, 4);
        Array.Copy(data, 0, crcInput, 4, data.Length);
        var crc = new byte[4];
        WriteBigEndianInt32(crc, 0, unchecked((int)Crc32(crcInput)));
        stream.Write(crc, 0, 4);
    }

    private static void WriteBigEndianInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
