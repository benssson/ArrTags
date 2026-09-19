using System;
using System.Globalization;

namespace ArrTags.Rendering;

/// <summary>
/// A provider-neutral 8-bit sRGB color used for the ADR-009 contrast check. It is
/// a small immutable value so the palette policy can be validated without a
/// raster library.
/// </summary>
public readonly struct RgbColor : IEquatable<RgbColor>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RgbColor"/> struct.
    /// </summary>
    /// <param name="red">The red channel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    public RgbColor(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>
    /// Gets the red channel.
    /// </summary>
    public byte Red { get; }

    /// <summary>
    /// Gets the green channel.
    /// </summary>
    public byte Green { get; }

    /// <summary>
    /// Gets the blue channel.
    /// </summary>
    public byte Blue { get; }

    /// <summary>
    /// Gets the WCAG relative luminance of this color.
    /// </summary>
    public double RelativeLuminance =>
        (0.2126 * Linearize(Red)) + (0.7152 * Linearize(Green)) + (0.0722 * Linearize(Blue));

    /// <summary>
    /// Determines whether two colors are equal.
    /// </summary>
    /// <param name="left">The left color.</param>
    /// <param name="right">The right color.</param>
    /// <returns><see langword="true"/> when the channels are equal.</returns>
    public static bool operator ==(RgbColor left, RgbColor right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two colors differ.
    /// </summary>
    /// <param name="left">The left color.</param>
    /// <param name="right">The right color.</param>
    /// <returns><see langword="true"/> when the channels differ.</returns>
    public static bool operator !=(RgbColor left, RgbColor right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Parses a <c>#RRGGBB</c> or <c>RRGGBB</c> hexadecimal color.
    /// </summary>
    /// <param name="text">The color text.</param>
    /// <param name="color">The parsed color when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the text is a valid color.</returns>
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        if (value.Length != 6)
        {
            return false;
        }

        if (byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(value[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            color = new RgbColor(red, green, blue);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes the WCAG contrast ratio between two colors, in the range 1:1 to
    /// 21:1.
    /// </summary>
    /// <param name="first">The first color.</param>
    /// <param name="second">The second color.</param>
    /// <returns>The contrast ratio.</returns>
    public static double ContrastRatio(RgbColor first, RgbColor second)
    {
        var lighter = Math.Max(first.RelativeLuminance, second.RelativeLuminance);
        var darker = Math.Min(first.RelativeLuminance, second.RelativeLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Determines whether this color equals another color.
    /// </summary>
    /// <param name="other">The other color.</param>
    /// <returns><see langword="true"/> when the channels are equal.</returns>
    public bool Equals(RgbColor other)
    {
        return Red == other.Red && Green == other.Green && Blue == other.Blue;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is RgbColor other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Red, Green, Blue);
    }

    /// <summary>
    /// Gets a hexadecimal <c>#RRGGBB</c> representation of this color.
    /// </summary>
    /// <returns>The hexadecimal color text.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"#{Red:X2}{Green:X2}{Blue:X2}");
    }

    private static double Linearize(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
