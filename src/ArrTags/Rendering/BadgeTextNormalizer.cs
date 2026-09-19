using System;
using System.Text;

namespace ArrTags.Rendering;

/// <summary>
/// Enforces the ADR-009 text limits before any draw or width-fitting work. The
/// normalizer removes non-whitespace control scalars, collapses whitespace runs
/// to one space, trims the ends, counts Unicode scalar values (not UTF-16
/// characters), and end-truncates with the policy ellipsis. Every operation is
/// provider-neutral and never splits a surrogate pair.
/// </summary>
public static class BadgeTextNormalizer
{
    /// <summary>
    /// Normalizes one candidate label and applies the bounded scalar-value
    /// limit. A label that normalizes to no visible text is omitted.
    /// </summary>
    /// <param name="text">The candidate label text. It is never mutated.</param>
    /// <param name="policy">The effective output policy that owns the text limits.</param>
    /// <returns>The bounded label, or <see cref="NormalizedBadgeText.Omitted"/> when no visible text remains.</returns>
    /// <exception cref="ArgumentNullException">The text or policy is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The policy text limits are not usable.</exception>
    public static NormalizedBadgeText Normalize(string text, RenderOutputPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(policy);
        ValidatePolicy(policy);

        var normalized = NormalizeCharacters(text, out var scalarCount);
        if (scalarCount == 0)
        {
            return NormalizedBadgeText.Omitted;
        }

        if (scalarCount <= policy.MaximumScalarValues)
        {
            return NormalizedBadgeText.FromNormalized(normalized, false);
        }

        var ellipsisScalarCount = CountScalarValues(policy.Ellipsis);
        var retained = Math.Min(policy.RetainedPrefixScalarValues, policy.MaximumScalarValues - ellipsisScalarCount);
        var truncated = TruncateToScalarValues(normalized, retained, policy.Ellipsis);
        return NormalizedBadgeText.FromNormalized(truncated, true);
    }

    /// <summary>
    /// Shortens already-normalized text to a bounded number of leading Unicode
    /// scalar values plus the truncation marker, using the same end-truncation
    /// rule as <see cref="Normalize"/>. It is used for the layout-stage width
    /// fit after the 24-scalar limit has already been applied. The marker is not
    /// appended when the text already fits within the requested scalar count, and
    /// a surrogate pair is never split.
    /// </summary>
    /// <param name="text">The already-normalized text. It is never mutated.</param>
    /// <param name="retainedScalarValues">The number of leading scalar values to retain, at least one.</param>
    /// <param name="ellipsis">The truncation marker, which must not be empty.</param>
    /// <returns>The shortened text, or the original text when it already fits.</returns>
    /// <exception cref="ArgumentNullException">The text or marker is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The retained scalar count is not positive.</exception>
    /// <exception cref="ArgumentException">The marker is empty.</exception>
    public static string Shorten(string text, int retainedScalarValues, string ellipsis)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ellipsis);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedScalarValues);

        if (ellipsis.Length == 0)
        {
            throw new ArgumentException("The truncation marker must not be empty.", nameof(ellipsis));
        }

        if (CountScalarValues(text) <= retainedScalarValues)
        {
            return text;
        }

        return TruncateToScalarValues(text, retainedScalarValues, ellipsis);
    }

    private static void ValidatePolicy(RenderOutputPolicy policy)
    {
        if (policy.MaximumScalarValues <= 0)
        {
            throw new ArgumentException("The output policy must allow at least one scalar value.", nameof(policy));
        }

        if (policy.RetainedPrefixScalarValues < 0)
        {
            throw new ArgumentException("The output policy retained prefix must not be negative.", nameof(policy));
        }

        if (string.IsNullOrEmpty(policy.Ellipsis))
        {
            throw new ArgumentException("The output policy ellipsis must not be empty.", nameof(policy));
        }

        if (CountScalarValues(policy.Ellipsis) > policy.MaximumScalarValues)
        {
            throw new ArgumentException("The output policy ellipsis must not exceed the scalar limit.", nameof(policy));
        }
    }

    private static string NormalizeCharacters(string text, out int scalarCount)
    {
        var builder = new StringBuilder(text.Length);
        var pendingWhitespace = false;
        var hasContent = false;
        var appendedScalars = 0;
        Span<char> runeBuffer = stackalloc char[2];

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                if (hasContent)
                {
                    pendingWhitespace = true;
                }

                continue;
            }

            if (Rune.IsControl(rune))
            {
                continue;
            }

            if (pendingWhitespace)
            {
                builder.Append(' ');
                pendingWhitespace = false;
                appendedScalars++;
            }

            var written = rune.EncodeToUtf16(runeBuffer);
            builder.Append(runeBuffer[..written]);
            hasContent = true;
            appendedScalars++;
        }

        scalarCount = appendedScalars;
        return builder.ToString();
    }

    private static string TruncateToScalarValues(string value, int retainedScalarValues, string ellipsis)
    {
        var builder = new StringBuilder();
        Span<char> runeBuffer = stackalloc char[2];
        var taken = 0;

        foreach (var rune in value.EnumerateRunes())
        {
            if (taken >= retainedScalarValues)
            {
                break;
            }

            var written = rune.EncodeToUtf16(runeBuffer);
            builder.Append(runeBuffer[..written]);
            taken++;
        }

        builder.Append(ellipsis);
        return builder.ToString();
    }

    private static int CountScalarValues(string value)
    {
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
