using System;
using System.Globalization;
using System.Text;

namespace ArrTags.Logging;

/// <summary>
/// Builds the bounded, secret-free log subject for an item-id-emitting call site
/// (ADR-026 clauses 1-2). When a primary media path is available the subject is
/// the final path component only, taken as the substring after the last <c>'/'</c>
/// or the last <c>'\'</c>, whichever is later, independently of host-platform
/// separator semantics; a directory, drive, share, or parent component is never
/// emitted on any host. Otherwise the subject is the Jellyfin item identifier in
/// <c>D</c> format. The file name is bounded to
/// <see cref="MaximumScalarValues"/> Unicode scalar values, has the documented
/// non-printing scalar categories removed, and is otherwise emitted verbatim; a
/// name that is empty, whitespace, or reduces to nothing after normalization
/// falls back to the item identifier. The helper is suitable for both
/// identity-bearing and item-id-only callers and never throws for a null, empty,
/// whitespace, or otherwise unusable path.
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
    /// <param name="primaryPath">The raw primary media path when available. Only its final path component is used (the substring after the last <c>'/'</c> or the last <c>'\'</c>, whichever is later); a directory, drive, share, or parent component is never emitted, independently of host-platform separator semantics.</param>
    /// <returns>The bounded, non-printing-free file-name subject, or the item identifier when the path yields no usable name.</returns>
    public static string Create(Guid jellyfinItemId, string? primaryPath)
    {
        var fallback = jellyfinItemId.ToString("D", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(primaryPath))
        {
            return fallback;
        }

        var fileName = ExtractFinalComponent(primaryPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fallback;
        }

        var bounded = NormalizeFileName(fileName);

        // The name can reduce to whitespace-only text once the non-printing
        // scalars are removed (for example " \u0001 "), so the fallback check is
        // on the normalized result as well as on the input.
        return string.IsNullOrWhiteSpace(bounded) ? fallback : bounded;
    }

    /// <summary>
    /// Returns the final path component: the substring after the last <c>'/'</c>
    /// and after the last <c>'\'</c>, whichever is later. Both separators are
    /// always recognized, so a POSIX path, a Windows path, a UNC path, and a
    /// mixed-separator path yield the same component on every host; a path whose
    /// last separator is trailing yields an empty component. This is deliberately
    /// not <c>Path.GetFileName</c>, whose separator set is host-platform
    /// dependent.
    /// </summary>
    /// <param name="path">The non-empty, non-whitespace primary media path.</param>
    /// <returns>The final path component, possibly empty.</returns>
    private static string ExtractFinalComponent(string path)
    {
        var lastSeparator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return lastSeparator < 0 ? path : path[(lastSeparator + 1)..];
    }

    /// <summary>
    /// Removes the documented non-printing scalar categories and truncates to the
    /// documented scalar bound in a single pass. The removal predicate is exactly
    /// the four non-printing categories <see cref="UnicodeCategory.Control"/>
    /// (Cc), <see cref="UnicodeCategory.Format"/> (Cf, including the zero-width,
    /// bidi, soft-hyphen, and isolate controls), <see
    /// cref="UnicodeCategory.LineSeparator"/> (Zl), and <see
    /// cref="UnicodeCategory.ParagraphSeparator"/> (Zp). No other category is
    /// removed, so letters, marks (including combining marks in Mn), space
    /// separators (Zs), private-use (Co), and unassigned (Cn) scalars are
    /// preserved. Every operation is on Unicode scalar values, never UTF-16 code
    /// units, so a surrogate pair is never split and non-ASCII text is otherwise
    /// preserved verbatim; whitespace is not collapsed or trimmed.
    /// </summary>
    /// <param name="fileName">The usable file-name component.</param>
    /// <returns>The bounded, non-printing-free subject.</returns>
    private static string NormalizeFileName(string fileName)
    {
        var builder = new StringBuilder(Math.Min(fileName.Length, MaximumScalarValues));
        Span<char> runeBuffer = stackalloc char[2];
        var appended = 0;

        foreach (var rune in fileName.EnumerateRunes())
        {
            if (IsNonPrintingScalar(rune))
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

    /// <summary>
    /// The documented non-printing removal predicate (ADR-026 clause 2 as
    /// hardened by the task 21.11 implementation note): Unicode Cc (Control), Cf
    /// (Format), Zl (Line Separator), and Zp (Paragraph Separator). It is a
    /// superset of the pre-fix <c>Rune.IsControl</c> (Cc-only) predicate.
    /// </summary>
    /// <param name="rune">The scalar to classify.</param>
    /// <returns><see langword="true"/> when the scalar is in a removal category.</returns>
    private static bool IsNonPrintingScalar(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.Control or
            UnicodeCategory.Format or
            UnicodeCategory.LineSeparator or
            UnicodeCategory.ParagraphSeparator;
    }
}
