using System;

namespace ArrTags.Rendering;

/// <summary>
/// The bounded result of normalizing and truncating one candidate badge label.
/// It is a provider-neutral display value: it carries only the final visible
/// text and whether truncation occurred, never a provider DTO, path, or
/// credential. An omitted result means the normalized text produced no visible
/// label, so the candidate must not be drawn.
/// </summary>
public sealed class NormalizedBadgeText
{
    private NormalizedBadgeText(string? text, bool wasTruncated)
    {
        Text = text;
        WasTruncated = wasTruncated;
    }

    /// <summary>
    /// Gets the shared result for a label that produced no visible text.
    /// </summary>
    public static NormalizedBadgeText Omitted { get; } = new NormalizedBadgeText(null, false);

    /// <summary>
    /// Gets the final visible label text, or <see langword="null"/> when the
    /// label is omitted.
    /// </summary>
    public string? Text { get; }

    /// <summary>
    /// Gets a value indicating whether the text was shortened by the scalar
    /// limit.
    /// </summary>
    public bool WasTruncated { get; }

    /// <summary>
    /// Gets a value indicating whether the label has no visible text and must
    /// not be drawn.
    /// </summary>
    public bool IsOmitted => Text is null;

    /// <summary>
    /// Creates a normalized label from non-empty bounded text.
    /// </summary>
    /// <param name="text">The non-empty normalized and bounded label text.</param>
    /// <param name="wasTruncated">Whether the text was shortened by the scalar limit.</param>
    /// <returns>A normalized, non-omitted label.</returns>
    /// <exception cref="ArgumentException">The text is <see langword="null"/> or empty.</exception>
    public static NormalizedBadgeText FromNormalized(string text, bool wasTruncated)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new NormalizedBadgeText(text, wasTruncated);
    }
}
