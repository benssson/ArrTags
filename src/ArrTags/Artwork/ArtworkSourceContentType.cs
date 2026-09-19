using System;

namespace ArrTags.Artwork;

/// <summary>
/// Detects and confines the V1 source image containers. The renderer's
/// color-profile inspection understands only PNG <c>iCCP</c> and JPEG
/// <c>APP2</c> profiles, so V1 accepts only those two containers and fails
/// closed for every other container (WebP, AVIF, GIF, BMP, or unrecognized
/// bytes). A profile embedded in an uninspected container can therefore never be
/// silently treated as sRGB.
/// </summary>
public static class ArtworkSourceContentType
{
    /// <summary>
    /// The confined PNG source content type.
    /// </summary>
    public const string Png = "image/png";

    /// <summary>
    /// The confined JPEG source content type.
    /// </summary>
    public const string Jpeg = "image/jpeg";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Detects a confined source content type from the exact bytes. Only the
    /// PNG and JPEG signatures are recognized; every other container returns
    /// <see langword="false"/> so the caller fails closed.
    /// </summary>
    /// <param name="bytes">The exact source bytes.</param>
    /// <param name="contentType">The detected content type when recognized.</param>
    /// <returns><see langword="true"/> when the container is a confined type.</returns>
    public static bool TryDetect(ReadOnlySpan<byte> bytes, out string contentType)
    {
        if (bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            contentType = Png;
            return true;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            contentType = Jpeg;
            return true;
        }

        contentType = string.Empty;
        return false;
    }

    /// <summary>
    /// Determines whether a declared content type is one of the inspected V1
    /// containers.
    /// </summary>
    /// <param name="contentType">The declared content type.</param>
    /// <returns><see langword="true"/> for <c>image/png</c> or <c>image/jpeg</c>.</returns>
    public static bool IsConfined(string? contentType)
    {
        return string.Equals(contentType, Png, StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, Jpeg, StringComparison.OrdinalIgnoreCase);
    }
}
