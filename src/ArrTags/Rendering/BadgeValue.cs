using System;

namespace ArrTags.Rendering;

/// <summary>
/// One provider-neutral display value resolved from canonical
/// <see cref="Metadata.BadgeMetadata"/>. A value exists only when its source
/// field is confirmed; unknown or absent fields produce no value and are never
/// rendered as a placeholder or an inferred negative.
/// </summary>
public sealed class BadgeValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadgeValue"/> class.
    /// </summary>
    /// <param name="selector">The provider-neutral selector that produced the value.</param>
    /// <param name="text">The normalized display text.</param>
    /// <exception cref="ArgumentOutOfRangeException">The selector is not defined.</exception>
    /// <exception cref="ArgumentException">The text is empty or whitespace.</exception>
    public BadgeValue(BadgeSelector selector, string text)
    {
        if (!Enum.IsDefined(selector))
        {
            throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown badge selector.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("A badge value requires display text.", nameof(text));
        }

        Selector = selector;
        Text = text;
        Kind = selector == BadgeSelector.UpgradePending
            ? BadgeValueKind.Status
            : BadgeValueKind.Technical;
    }

    /// <summary>
    /// Gets the provider-neutral selector that produced the value.
    /// </summary>
    public BadgeSelector Selector { get; }

    /// <summary>
    /// Gets the placement role of the value.
    /// </summary>
    public BadgeValueKind Kind { get; }

    /// <summary>
    /// Gets the normalized display text.
    /// </summary>
    public string Text { get; }
}
