using System;

namespace ArrTags.Rendering;

/// <summary>
/// One computed pill position in output pixels. A placement is provider-neutral
/// geometry: it carries the resolved selector and final visible text, whether it
/// is the status pill, its rail row (or <c>-1</c> for the status pill), and its
/// scaled rectangle. It never carries a source pixel, provider payload, path, or
/// credential.
/// </summary>
public sealed class BadgePillPlacement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadgePillPlacement"/> class.
    /// </summary>
    /// <param name="selector">The selector that produced the value.</param>
    /// <param name="text">The final visible, bounded text.</param>
    /// <param name="isStatus">Whether this is the independent status pill.</param>
    /// <param name="row">The rail row, or <c>-1</c> for the status pill.</param>
    /// <param name="x">The left edge in output pixels.</param>
    /// <param name="y">The top edge in output pixels.</param>
    /// <param name="width">The pill width in output pixels.</param>
    /// <param name="height">The pill height in output pixels.</param>
    /// <exception cref="ArgumentNullException">The text is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The selector is not defined or a rectangle value is not positive.</exception>
    public BadgePillPlacement(
        BadgeSelector selector,
        string text,
        bool isStatus,
        int row,
        double x,
        double y,
        double width,
        double height)
    {
        if (!Enum.IsDefined(selector))
        {
            throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown badge selector.");
        }

        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Selector = selector;
        Text = text;
        IsStatus = isStatus;
        Row = row;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the selector that produced the value.
    /// </summary>
    public BadgeSelector Selector { get; }

    /// <summary>
    /// Gets the final visible, bounded text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets a value indicating whether this is the independent status pill.
    /// </summary>
    public bool IsStatus { get; }

    /// <summary>
    /// Gets the rail row, or <c>-1</c> for the status pill.
    /// </summary>
    public int Row { get; }

    /// <summary>
    /// Gets the left edge in output pixels.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the top edge in output pixels.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Gets the pill width in output pixels.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the pill height in output pixels.
    /// </summary>
    public double Height { get; }
}
