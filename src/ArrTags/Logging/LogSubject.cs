using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ArrTags.Logging;

/// <summary>
/// Builds the bounded, secret-free log subject for an item-id-emitting call site
/// (ADR-026 clauses 1-2). When a primary media path is available the subject is
/// the file-name component only (<c>Path.GetFileName</c>), never a directory,
/// drive, share, or parent path; otherwise the subject is the Jellyfin item
/// identifier in <c>D</c> format. The file name is bounded to
/// <see cref="MaximumScalarValues"/> Unicode scalar values, has control scalars
/// removed, and is otherwise emitted verbatim; a name that is empty, whitespace,
/// or reduces to nothing after normalization falls back to the item identifier.
/// The helper is suitable for both identity-bearing and item-id-only callers and
/// never throws for a null, empty, whitespace, or otherwise unusable path.
/// </summary>
public static class LogSubject
{
    /// <summary>
    /// The documented maximum length of the file-name subject in Unicode scalar
    /// values (ADR-026 clause 2). The bound is code-owned, not configurable.
    /// </summary>
    public const int MaximumScalarValues = 128;

    /// <summary>
    /// Builds the log subject for a call site that has only the Jellyfin item
    /// identifier, such as the explicit item-id fallback sites (ADR-026 clause 3).
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier; it is emitted in <c>D</c> format.</param>
    /// <returns>The bounded item-identifier subject.</returns>
    public static string Create(Guid jellyfinItemId)
    {
        return Create(jellyfinItemId, null);
    }

    /// <summary>
    /// Builds the log subject for a call site that has a primary media path when
    /// one exists, falling back to the item identifier when no usable file name
    /// is available.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier; it is the fallback subject and is emitted in <c>D</c> format.</param>
    /// <param name="primaryPath">The raw primary media path when available. Only its file-name component is used; a directory, drive, share, or parent component is never emitted.</param>
    /// <returns>The bounded, control-free file-name subject, or the item identifier when the path yields no usable name.</returns>
    public static string Create(Guid jellyfinItemId, string? primaryPath)
    {
        var fallback = jellyfinItemId.ToString("D", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(primaryPath))
        {
            return fallback;
        }

        var fileName = Path.GetFileName(primaryPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fallback;
        }

        var bounded = NormalizeFileName(fileName);

        // The name can reduce to whitespace-only text once controls are removed
        // (for example " \u0001 "), so the fallback check is on the normalized
        // result as well as on the input.
        return string.IsNullOrWhiteSpace(bounded) ? fallback : bounded;
    }

    /// <summary>
    /// Removes control scalars and truncates to the documented scalar bound in a
    /// single pass. Every operation is on Unicode scalar values, never UTF-16
    /// code units, so a surrogate pair is never split and non-ASCII text is
    /// otherwise preserved verbatim; whitespace is not collapsed or trimmed.
    /// </summary>
    /// <param name="fileName">The usable file-name component.</param>
    /// <returns>The bounded, control-free subject.</returns>
    private static string NormalizeFileName(string fileName)
    {
        var builder = new StringBuilder(Math.Min(fileName.Length, MaximumScalarValues));
        Span<char> runeBuffer = stackalloc char[2];
        var appended = 0;

        foreach (var rune in fileName.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                continue;
            }

            if (appended == MaximumScalarValues)
            {
                break;
            }

            var written = rune.EncodeToUtf16(runeBuffer);
            builder.Append(runeBuffer[..written]);
            appended++;
        }

        return builder.ToString();
    }
}
