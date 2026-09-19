using System;
using System.Security.Cryptography;

namespace ArrTags.Rendering;

/// <summary>
/// The immutable, bounded, read-only description of one retained source image.
/// It carries the exact source bytes, content type, oriented dimensions, and
/// verified SHA-256 identity, and it never contains a filesystem path, Jellyfin
/// entity, provider DTO, network handle, credential, or mutable image object.
/// The renderer treats these bytes as read-only and never mutates them.
/// </summary>
public sealed class SourceImageInput
{
    private readonly byte[] _bytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceImageInput"/> class and
    /// verifies that the declared SHA-256 identity matches the supplied bytes.
    /// The bytes are copied so the descriptor cannot be changed by its caller.
    /// </summary>
    /// <param name="bytes">The exact source image bytes.</param>
    /// <param name="contentType">The source media type, for example <c>image/png</c>.</param>
    /// <param name="orientedWidth">The source width in pixels after orientation.</param>
    /// <param name="orientedHeight">The source height in pixels after orientation.</param>
    /// <param name="sourceSha256">The 64-character hexadecimal SHA-256 of the exact bytes.</param>
    /// <exception cref="ArgumentException">The bytes, content type, or hash are missing or invalid, or the hash does not match the bytes.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An oriented dimension is not positive.</exception>
    public SourceImageInput(
        ReadOnlySpan<byte> bytes,
        string contentType,
        int orientedWidth,
        int orientedHeight,
        string sourceSha256)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("A source image input requires at least one byte.", nameof(bytes));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A source image content type must start with 'image/'.", nameof(contentType));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orientedWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orientedHeight);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);

        if (sourceSha256.Length != 64 || !IsHexadecimal(sourceSha256))
        {
            throw new ArgumentException("A source image input requires a 64-character SHA-256 hex value.", nameof(sourceSha256));
        }

        _bytes = bytes.ToArray();
        var actual = ComputeSha256(_bytes);
        if (!string.Equals(actual, sourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The declared source SHA-256 does not match the supplied bytes.", nameof(sourceSha256));
        }

        ContentType = contentType;
        OrientedWidth = orientedWidth;
        OrientedHeight = orientedHeight;
        SourceSha256 = actual;
    }

    /// <summary>
    /// Gets the exact source bytes as a read-only value. The renderer never
    /// mutates these bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    /// <summary>
    /// Gets the source media type.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the source width in pixels after orientation.
    /// </summary>
    public int OrientedWidth { get; }

    /// <summary>
    /// Gets the source height in pixels after orientation.
    /// </summary>
    public int OrientedHeight { get; }

    /// <summary>
    /// Gets the uppercase SHA-256 identity of the exact source bytes.
    /// </summary>
    public string SourceSha256 { get; }

    /// <summary>
    /// Computes the uppercase SHA-256 identity of the supplied bytes. It is used
    /// by the host boundary to build a valid descriptor and by the descriptor to
    /// verify the declared identity.
    /// </summary>
    /// <param name="bytes">The exact bytes to hash.</param>
    /// <returns>The uppercase 64-character SHA-256 hex value.</returns>
    public static string ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool IsHexadecimal(string value)
    {
        foreach (var character in value)
        {
            var isHex = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
