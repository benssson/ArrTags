using System;
using System.Security.Cryptography;

namespace ArrTags.Artwork;

/// <summary>
/// Generates and validates the opaque ArrTags ownership and publication tokens.
/// The tokens are random plugin-state identifiers. They are not embedded in any
/// image, are never returned by Jellyfin, and are not Jellyfin guarantees;
/// ownership is proven only by a fresh active-image comparison. The tokens are
/// deliberately bounded and use only a safe character set so they can never
/// become a path or payload hazard.
/// </summary>
public static class ArtworkTokens
{
    /// <summary>
    /// The number of random bytes in a generated token.
    /// </summary>
    public const int TokenByteLength = 32;

    /// <summary>
    /// Creates a new opaque random token.
    /// </summary>
    /// <returns>A 64-character upper-case hexadecimal token.</returns>
    public static string Create()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenByteLength));
    }

    /// <summary>
    /// Determines whether the value is a well-formed opaque token.
    /// </summary>
    /// <param name="value">The candidate token.</param>
    /// <returns><see langword="true"/> when the token is non-empty, bounded, and uses only safe characters.</returns>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length is < 16 or > 128)
        {
            return false;
        }

        foreach (var character in value)
        {
            var isSafe = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || character == '-'
                || character == '_';
            if (!isSafe)
            {
                return false;
            }
        }

        return true;
    }
}
