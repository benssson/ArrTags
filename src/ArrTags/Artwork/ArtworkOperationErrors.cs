using System;
using System.Text;

namespace ArrTags.Artwork;

/// <summary>
/// Redacts and bounds the optional diagnostic text stored on an
/// <see cref="ArtworkOperation"/>. The operation journal is authoritative,
/// durable state, so a diagnostic must never carry a credential, a media path, a
/// raw external payload, or unbounded text. Callers supply an already
/// redacted summary; this helper enforces the remaining contract by stripping
/// control characters (which could smuggle raw payload bytes or log-injection
/// sequences) and truncating to a fixed bound.
/// </summary>
public static class ArtworkOperationErrors
{
    /// <summary>
    /// The maximum retained diagnostic length.
    /// </summary>
    public const int MaxErrorLength = 512;

    /// <summary>
    /// Redacts and bounds an optional diagnostic summary.
    /// </summary>
    /// <param name="value">The candidate diagnostic summary.</param>
    /// <returns>A bounded, control-free summary, or <see langword="null"/> when nothing usable remains.</returns>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        if (builder.Length == 0)
        {
            return null;
        }

        return builder.Length <= MaxErrorLength
            ? builder.ToString()
            : builder.ToString(0, MaxErrorLength);
    }
}
