using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Rendering;
using SkiaSharp;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 synthetic image fixtures. Every fixture is generated
/// deterministically in code from solid colors and simple shapes; no external,
/// copyrighted, or poster artwork is read or committed. The Skia-backed builders
/// are only called from environment-guarded tests.
/// </summary>
internal static class RenderImageFixtures
{
    /// <summary>The opaque fixture background red channel.</summary>
    public const int BackgroundRed = 0x20;

    /// <summary>The opaque fixture background green channel.</summary>
    public const int BackgroundGreen = 0x40;

    /// <summary>The opaque fixture background blue channel.</summary>
    public const int BackgroundBlue = 0x80;

    /// <summary>
    /// Builds canonical badge metadata with independently optional technical
    /// values so each golden fixture can exercise a specific field set.
    /// </summary>
    public static BadgeMetadata BuildMetadata(
        string? quality = "Bluray-1080p",
        string? resolution = "1080p",
        ArrDynamicRangeKind? dynamicRangeKind = null,
        bool? dolbyVision = null,
        string? source = "WEB-DL",
        string? videoCodec = "x265",
        string? audioCodec = null,
        double? audioChannels = null,
        IReadOnlyList<ArrAudioFeature>? audioFeatures = null,
        bool? upgradePending = null,
        IReadOnlyList<string>? customBadges = null,
        DateTimeOffset? observedAt = null)
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
        var recordIdentity = new RadarrIdentity(connectionId, 7, ArrFileIdentity.Present(42));

        return new BadgeMetadata(
            provider,
            recordIdentity,
            observedAt ?? RenderTestFixtures.ObservedAt,
            quality: quality is null
                ? null
                : new ArrQualityDescriptor(quality, "bluray", 1080, "none", 7),
            resolution: resolution is null
                ? null
                : new ArrResolutionDescriptor(1920, 1080, resolution, ArrMetadataOrigin.ProviderMediaInfo),
            dynamicRange: dynamicRangeKind is null
                ? null
                : new ArrDynamicRangeDescriptor(dynamicRangeKind.Value, null, ArrMetadataOrigin.ProviderMediaInfo),
            dolbyVision: dolbyVision,
            videoCodec: videoCodec,
            audioCodec: audioCodec,
            audioChannels: audioChannels,
            audioFeatures: audioFeatures,
            source: source,
            upgradePending: upgradePending,
            customBadges: customBadges);
    }

    /// <summary>
    /// Encodes an opaque JPEG source of the requested dimensions.
    /// </summary>
    public static SourceImageInput CreateOpaqueJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque,
            SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(BackgroundRed, BackgroundGreen, BackgroundBlue));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return Source(data.ToArray(), width, height);
    }

    /// <summary>
    /// Encodes an opaque RGB PNG source of the requested dimensions.
    /// </summary>
    public static SourceImageInput CreateRgbPng(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgb888x,
            SKAlphaType.Opaque,
            SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(BackgroundRed, BackgroundGreen, BackgroundBlue));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Source(data.ToArray(), width, height);
    }

    /// <summary>
    /// Encodes an RGBA PNG source with a fully transparent top region so the
    /// decoded image has meaningful alpha.
    /// </summary>
    public static SourceImageInput CreateRgbaPng(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb()));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(BackgroundRed, BackgroundGreen, BackgroundBlue, 255));
            using var transparent = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 0),
                BlendMode = SKBlendMode.Src,
            };
            canvas.DrawRect(new SKRect(0, 0, width, height / 4f), transparent);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Source(data.ToArray(), width, height);
    }

    /// <summary>
    /// Builds a source descriptor over the supplied PNG bytes.
    /// </summary>
    public static SourceImageInput Source(byte[] bytes, int width, int height)
    {
        return new SourceImageInput(bytes, "image/png", width, height, SourceImageInput.ComputeSha256(bytes));
    }

    /// <summary>The corner-marker edge length in pixels.</summary>
    public const int MarkerSize = 80;

    /// <summary>The red top-left marker red channel.</summary>
    public const int RedMarkerRed = 0xE0;

    /// <summary>The red top-left marker green channel.</summary>
    public const int RedMarkerGreen = 0x10;

    /// <summary>The red top-left marker blue channel.</summary>
    public const int RedMarkerBlue = 0x10;

    /// <summary>The green top-right marker red channel.</summary>
    public const int GreenMarkerRed = 0x10;

    /// <summary>The green top-right marker green channel.</summary>
    public const int GreenMarkerGreen = 0xB0;

    /// <summary>The green top-right marker blue channel.</summary>
    public const int GreenMarkerBlue = 0x10;

    /// <summary>
    /// Encodes an asymmetric opaque JPEG with a red top-left corner marker, then
    /// inserts the requested EXIF orientation tag. The marker makes an applied
    /// rotation observable in a golden pixel plane.
    /// </summary>
    public static byte[] CreateMarkedOrientedJpeg(int width, int height, ushort orientation)
    {
        using var bitmap = NewMarkerBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(BackgroundRed, BackgroundGreen, BackgroundBlue));
            using var paint = new SKPaint { Color = new SKColor(RedMarkerRed, RedMarkerGreen, RedMarkerBlue) };
            canvas.DrawRect(new SKRect(0, 0, MarkerSize, MarkerSize), paint);
        }

        return EncodeOrientedJpeg(bitmap, orientation);
    }

    /// <summary>
    /// Encodes an opaque JPEG with a red top-left marker and a green top-right
    /// marker, then inserts the requested EXIF orientation tag. The two distinct
    /// corners make every orientation's content placement observable.
    /// </summary>
    public static byte[] CreateTwoMarkerOrientedJpeg(int width, int height, ushort orientation)
    {
        using var bitmap = NewMarkerBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(BackgroundRed, BackgroundGreen, BackgroundBlue));
            using var red = new SKPaint { Color = new SKColor(RedMarkerRed, RedMarkerGreen, RedMarkerBlue) };
            using var green = new SKPaint { Color = new SKColor(GreenMarkerRed, GreenMarkerGreen, GreenMarkerBlue) };
            canvas.DrawRect(new SKRect(0, 0, MarkerSize, MarkerSize), red);
            canvas.DrawRect(new SKRect(width - MarkerSize, 0, width, MarkerSize), green);
        }

        return EncodeOrientedJpeg(bitmap, orientation);
    }

    /// <summary>
    /// Builds a source descriptor over the supplied JPEG bytes.
    /// </summary>
    public static SourceImageInput JpegSource(byte[] bytes, int width, int height)
    {
        return new SourceImageInput(bytes, "image/jpeg", width, height, SourceImageInput.ComputeSha256(bytes));
    }

    /// <summary>
    /// Returns the PNG color type byte (2 = RGB, 6 = RGBA) of an encoded PNG.
    /// </summary>
    public static int ReadPngColorType(byte[] png) => png[25];

    /// <summary>
    /// Builds a minimal but structurally valid RGB ICC profile that the pinned
    /// Skia color engine parses and converts to sRGB. It is generated from fixed
    /// arithmetic so the bytes are deterministic and repository-owned.
    /// </summary>
    public static byte[] BuildSrgbLikeIcc()
    {
        var signatures = new[] { "rXYZ", "gXYZ", "bXYZ", "wtpt", "rTRC", "gTRC", "bTRC" };
        var tagData = new byte[signatures.Length][];
        tagData[0] = Xyz(0.4360, 0.2225, 0.0139);
        tagData[1] = Xyz(0.3851, 0.7169, 0.0971);
        tagData[2] = Xyz(0.1431, 0.0606, 0.7139);
        tagData[3] = Xyz(0.9642, 1.0, 0.8249);
        for (var index = 4; index < signatures.Length; index++)
        {
            tagData[index] = Curve(2.2);
        }

        var dataStart = 128 + 4 + (signatures.Length * 12);
        var offsets = new int[signatures.Length];
        var cursor = dataStart;
        for (var index = 0; index < signatures.Length; index++)
        {
            cursor = (cursor + 3) & ~3;
            offsets[index] = cursor;
            cursor += tagData[index].Length;
        }

        var profile = new byte[cursor];
        WriteBigEndianInt32(profile, 0, profile.Length);
        WriteBigEndianInt32(profile, 8, 0x02100000);
        WriteAscii(profile, 12, "mntr");
        WriteAscii(profile, 16, "RGB ");
        WriteAscii(profile, 20, "XYZ ");
        WriteAscii(profile, 36, "acsp");
        WriteAscii(profile, 80, "    ");
        WriteBigEndianInt32(profile, 68, 0x0000F6D6);
        WriteBigEndianInt32(profile, 72, 0x00010000);
        WriteBigEndianInt32(profile, 76, 0x0000D32D);
        WriteBigEndianInt32(profile, 128, signatures.Length);
        var entry = 132;
        for (var index = 0; index < signatures.Length; index++)
        {
            WriteAscii(profile, entry, signatures[index]);
            WriteBigEndianInt32(profile, entry + 4, offsets[index]);
            WriteBigEndianInt32(profile, entry + 8, tagData[index].Length);
            entry += 12;
        }

        for (var index = 0; index < signatures.Length; index++)
        {
            Array.Copy(tagData[index], 0, profile, offsets[index], tagData[index].Length);
        }

        return profile;
    }

    /// <summary>
    /// Returns base PNG bytes with the supplied iCCP profile chunk inserted after
    /// the IHDR chunk.
    /// </summary>
    public static byte[] InsertPngIccp(byte[] png, byte[] iccProfile)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(iccProfile, 0, iccProfile.Length);
        }

        var keyword = Encoding.ASCII.GetBytes("ICC");
        var compressedBytes = compressed.ToArray();
        var payload = new byte[keyword.Length + 2 + compressedBytes.Length];
        Array.Copy(keyword, 0, payload, 0, keyword.Length);
        payload[keyword.Length] = 0;
        payload[keyword.Length + 1] = 0;
        Array.Copy(compressedBytes, 0, payload, keyword.Length + 2, compressedBytes.Length);
        return InsertPngChunkAfterIhdr(png, "iCCP", payload);
    }

    /// <summary>
    /// Builds an RGB PNG source that carries tEXt, tIME, and eXIf ancillary
    /// chunks, so the PNG-contract tests can prove the renderer strips them.
    /// </summary>
    public static SourceImageInput CreateMetadataBearingPng(int width, int height)
    {
        var png = CreateRgbPng(width, height).Bytes.ToArray();
        var text = Encoding.ASCII.GetBytes("Comment\0synthetic-fixture");
        var time = new byte[7];
        var exif = new byte[26];
        WriteBigEndianInt32(exif, 0, 0x49492A00);
        exif[4] = 0x08;
        var withText = InsertPngChunkAfterIhdr(png, "tEXt", text);
        var withTime = InsertPngChunkAfterIhdr(withText, "tIME", time);
        var withExif = InsertPngChunkAfterIhdr(withTime, "eXIf", exif);
        return Source(withExif, width, height);
    }

    /// <summary>
    /// Inserts a PNG chunk immediately after the IHDR chunk.
    /// </summary>
    public static byte[] InsertPngChunkAfterIhdr(byte[] png, string type, byte[] payload)
    {
        var ihdrLength = RendererBehaviorFixtures.ReadBigEndianInt32(png, 8);
        var ihdrEnd = 8 + 12 + ihdrLength;
        using var output = new MemoryStream();
        output.Write(png, 0, ihdrEnd);
        WritePngChunk(output, type, payload);
        output.Write(png, ihdrEnd, png.Length - ihdrEnd);
        return output.ToArray();
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

    private static byte[] Xyz(double x, double y, double z)
    {
        var bytes = new byte[20];
        WriteAscii(bytes, 0, "XYZ ");
        WriteBigEndianInt32(bytes, 8, ToS15Fixed16(x));
        WriteBigEndianInt32(bytes, 12, ToS15Fixed16(y));
        WriteBigEndianInt32(bytes, 16, ToS15Fixed16(z));
        return bytes;
    }

    private static byte[] Curve(double gamma)
    {
        var bytes = new byte[16];
        WriteAscii(bytes, 0, "curv");
        WriteBigEndianInt32(bytes, 8, 1);
        var value = (int)Math.Round(gamma * 256.0);
        bytes[12] = (byte)(value >> 8);
        bytes[13] = (byte)value;
        return bytes;
    }

    private static SKBitmap NewMarkerBitmap(int width, int height)
    {
        return new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque,
            SKColorSpace.CreateSrgb()));
    }

    private static byte[] EncodeOrientedJpeg(SKBitmap bitmap, ushort orientation)
    {
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

    private static int ToS15Fixed16(double value) => (int)Math.Round(value * 65536.0);

    private static void WriteAscii(byte[] bytes, int offset, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            bytes[offset + index] = (byte)value[index];
        }
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
