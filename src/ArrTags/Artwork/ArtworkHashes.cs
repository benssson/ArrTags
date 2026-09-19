using System;
using System.Security.Cryptography;

namespace ArrTags.Artwork;

/// <summary>
/// Shared SHA-256 helpers for artwork provenance. Hashes are always represented
/// as upper-case hexadecimal so equality is ordinal and stable across platforms.
/// </summary>
public static class ArtworkHashes
{
    /// <summary>
    /// The length of a hexadecimal SHA-256 string.
    /// </summary>
    public const int Sha256HexLength = 64;

    /// <summary>
    /// Computes the upper-case SHA-256 of the supplied bytes.
    /// </summary>
    /// <param name="bytes">The bytes to hash.</param>
    /// <returns>The 64-character upper-case hexadecimal SHA-256.</returns>
    public static string ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Determines whether the value is a 64-character hexadecimal SHA-256.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when the value is a hexadecimal SHA-256.</returns>
    public static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != Sha256HexLength)
        {
            return false;
        }

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
