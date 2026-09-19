using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace ArrTags.Rendering;

/// <summary>
/// The bounded, format-aware source color-profile inspection used by the ADR-010
/// sRGB contract. An input without an embedded ICC profile is treated as sRGB; a
/// recognized PNG <c>iCCP</c> or JPEG <c>APP2</c> profile must parse into a color
/// space the pinned renderer can convert, or the render fails closed rather than
/// silently treating the bytes as sRGB. A container that is not PNG or JPEG, or
/// that does not expose a complete profile, is left to the decoder's normal
/// bounded failure handling. This type never exposes profile bytes or decoder
/// detail outside the renderer.
/// </summary>
internal static class SourceColorProfile
{
    /// <summary>
    /// The maximum decompressed ICC profile size. A profile larger than this is
    /// treated as invalid so a compressed profile cannot cause an unbounded
    /// allocation.
    /// </summary>
    private const int MaximumIccProfileBytes = 16 * 1024 * 1024;

    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private static readonly byte[] PngIccpChunk = Encoding.ASCII.GetBytes("iCCP");

    private static readonly byte[] PngIendChunk = Encoding.ASCII.GetBytes("IEND");

    private static readonly byte[] JpegIccIdentifier = Encoding.ASCII.GetBytes("ICC_PROFILE\0");

    /// <summary>
    /// Inspects the exact encoded source bytes for an embedded color profile.
    /// </summary>
    /// <param name="bytes">The exact encoded source image bytes.</param>
    /// <returns>The bounded profile inspection result.</returns>
    public static SourceColorProfileKind Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return InspectPng(bytes);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return InspectJpeg(bytes);
        }

        return SourceColorProfileKind.None;
    }

    private static SourceColorProfileKind InspectPng(ReadOnlySpan<byte> bytes)
    {
        var offset = PngSignature.Length;
        byte[]? profile = null;

        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > (uint)(int.MaxValue - 12))
            {
                return SourceColorProfileKind.None;
            }

            var chunkLength = (int)length;
            if (offset + 12 + chunkLength > bytes.Length)
            {
                return SourceColorProfileKind.None;
            }

            var type = bytes.Slice(offset + 4, 4);
            var data = bytes.Slice(offset + 8, chunkLength);

            if (type.SequenceEqual(PngIccpChunk))
            {
                if (profile is not null)
                {
                    return SourceColorProfileKind.Invalid;
                }

                profile = ExtractPngProfile(data);
                if (profile is null)
                {
                    return SourceColorProfileKind.Invalid;
                }
            }

            if (type.SequenceEqual(PngIendChunk))
            {
                break;
            }

            offset += 12 + chunkLength;
        }

        return profile is null
            ? SourceColorProfileKind.None
            : IsSupportedIccProfile(profile) ? SourceColorProfileKind.Supported : SourceColorProfileKind.Invalid;
    }

    private static byte[]? ExtractPngProfile(ReadOnlySpan<byte> data)
    {
        // A PNG iCCP chunk is a 1-79 byte keyword, a null separator, a one-byte
        // compression method (0 = zlib), and the compressed profile.
        var keywordLimit = Math.Min(data.Length, 80);
        var keywordEnd = -1;
        for (var index = 0; index < keywordLimit; index++)
        {
            if (data[index] == 0)
            {
                keywordEnd = index;
                break;
            }
        }

        if (keywordEnd < 1)
        {
            return null;
        }

        var methodIndex = keywordEnd + 1;
        if (methodIndex >= data.Length || data[methodIndex] != 0)
        {
            return null;
        }

        var compressed = data[(methodIndex + 1)..];
        return compressed.IsEmpty ? null : TryDecompress(compressed);
    }

    private static byte[]? TryDecompress(ReadOnlySpan<byte> compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaximumIccProfileBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            var profile = output.ToArray();
            return profile.Length == 0 ? null : profile;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static SourceColorProfileKind InspectJpeg(ReadOnlySpan<byte> bytes)
    {
        var offset = 2;
        var segments = new SortedDictionary<int, byte[]>();
        var declaredCount = -1;

        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                return SourceColorProfileKind.None;
            }

            var marker = bytes[offset + 1];
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            // Standalone markers carry no length field.
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }

            // Start-of-scan or end-of-image ends the segment walk.
            if (marker == 0xDA || marker == 0xD9)
            {
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
            if (length < 2 || offset + 2 + length > bytes.Length)
            {
                return SourceColorProfileKind.None;
            }

            var payload = bytes.Slice(offset + 4, length - 2);
            if (marker == 0xE2 && payload.StartsWith(JpegIccIdentifier))
            {
                if (payload.Length <= 14)
                {
                    return SourceColorProfileKind.Invalid;
                }

                var sequence = payload[12];
                var count = payload[13];
                if (count == 0 || sequence == 0 || sequence > count)
                {
                    return SourceColorProfileKind.Invalid;
                }

                if (declaredCount != -1 && declaredCount != count)
                {
                    return SourceColorProfileKind.Invalid;
                }

                declaredCount = count;
                if (segments.ContainsKey(sequence))
                {
                    return SourceColorProfileKind.Invalid;
                }

                segments[sequence] = payload[14..].ToArray();
            }

            offset += 2 + length;
        }

        if (segments.Count == 0)
        {
            return SourceColorProfileKind.None;
        }

        if (declaredCount < 0 || segments.Count != declaredCount)
        {
            return SourceColorProfileKind.Invalid;
        }

        var total = 0;
        foreach (var segment in segments.Values)
        {
            total += segment.Length;
            if (total > MaximumIccProfileBytes)
            {
                return SourceColorProfileKind.Invalid;
            }
        }

        var profile = new byte[total];
        var cursor = 0;
        foreach (var segment in segments.Values)
        {
            segment.CopyTo(profile, cursor);
            cursor += segment.Length;
        }

        return IsSupportedIccProfile(profile) ? SourceColorProfileKind.Supported : SourceColorProfileKind.Invalid;
    }

    private static bool IsSupportedIccProfile(byte[] profile)
    {
        try
        {
            using var colorSpace = SKColorSpace.CreateIcc(profile);
            return colorSpace is not null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
