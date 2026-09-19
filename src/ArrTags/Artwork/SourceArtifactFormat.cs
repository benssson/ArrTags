using System;

namespace ArrTags.Artwork;

/// <summary>
/// Bounded MIME-type and magic-byte validation for retained source artifacts.
/// The store accepts the image formats Jellyfin serves for a poster, normalizes
/// the common non-standard <c>image/jpg</c> alias, and rejects a declared type
/// that contradicts a recognizable file signature.
/// </summary>
public static class SourceArtifactFormat
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Normalizes and validates a declared source MIME type.
    /// </summary>
    /// <param name="contentType">The declared MIME type.</param>
    /// <param name="normalized">The normalized MIME type when valid.</param>
    /// <returns><see langword="true"/> when the MIME type is a supported image type.</returns>
    public static bool TryNormalizeContentType(string? contentType, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128)
        {
            return false;
        }

        var candidate = contentType.Trim().ToLowerInvariant();
        candidate = candidate switch
        {
            "image/jpg" => "image/jpeg",
            "image/x-png" => "image/png",
            _ => candidate,
        };

        var supported = candidate is "image/png"
            or "image/jpeg"
            or "image/gif"
            or "image/webp"
            or "image/bmp";
        if (!supported)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// Detects the MIME type from a recognizable magic-byte signature.
    /// </summary>
    /// <param name="bytes">The artifact bytes.</param>
    /// <returns>The detected MIME type, or <see langword="null"/> when the signature is not recognized.</returns>
    public static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (StartsWithAscii(bytes, "GIF87a") || StartsWithAscii(bytes, "GIF89a"))
        {
            return "image/gif";
        }

        if (bytes.Length >= 12
            && StartsWithAscii(bytes, "RIFF")
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return "image/bmp";
        }

        return null;
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> bytes, string value)
    {
        if (bytes.Length < value.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (bytes[index] != (byte)value[index])
            {
                return false;
            }
        }

        return true;
    }
}
