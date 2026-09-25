using System;
using System.Globalization;

namespace ArrTags.Configuration;

/// <summary>
/// The ADR-028 code-owned display mapping for one byte-denominated operational
/// limit: a fixed binary unit, a fixed step, and a fixed display precision. The
/// step divides the field's minimum, default, and maximum exactly, so every
/// bound round-trips through the settings page without losing a byte. The
/// internal and persisted representation stays bytes; only the dashboard
/// settings page presents the value in this unit.
/// <see cref="FormatDisplay"/> renders stored bytes at the field's precision with
/// trailing zeros trimmed, and <see cref="TryParseDisplay"/> converts a displayed
/// decimal back to bytes, rejecting a value that does not land on the field's
/// step rather than silently rounding it. The byte range is not re-checked here:
/// it stays owned by the unchanged <see cref="OperationalLimits.Validate"/> and
/// the page's native input bounds.
/// </summary>
public sealed class ByteLimitField
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ByteLimitField"/> class.
    /// </summary>
    /// <param name="propertyName">The <see cref="OperationalLimits"/> property name.</param>
    /// <param name="unitLabel">The fixed binary display unit label.</param>
    /// <param name="unitBytes">The byte size of one display unit.</param>
    /// <param name="stepBytes">The fixed display step in bytes.</param>
    /// <param name="displayPrecision">The fixed number of fractional display digits.</param>
    /// <param name="minimumBytes">The validated minimum in bytes.</param>
    /// <param name="defaultBytes">The default in bytes.</param>
    /// <param name="maximumBytes">The validated maximum in bytes.</param>
    public ByteLimitField(
        string propertyName,
        string unitLabel,
        long unitBytes,
        long stepBytes,
        int displayPrecision,
        long minimumBytes,
        long defaultBytes,
        long maximumBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        ArgumentException.ThrowIfNullOrEmpty(unitLabel);

        PropertyName = propertyName;
        UnitLabel = unitLabel;
        UnitBytes = unitBytes;
        StepBytes = stepBytes;
        DisplayPrecision = displayPrecision;
        MinimumBytes = minimumBytes;
        DefaultBytes = defaultBytes;
        MaximumBytes = maximumBytes;
        PageElementId = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }

    /// <summary>
    /// Gets the <see cref="OperationalLimits"/> property name this mapping
    /// describes.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the settings-page element id for the field (the property name with a
    /// lower-case first character).
    /// </summary>
    public string PageElementId { get; }

    /// <summary>
    /// Gets the fixed binary display unit label (<c>MB</c> or <c>KB</c>).
    /// </summary>
    public string UnitLabel { get; }

    /// <summary>
    /// Gets the number of bytes in one display unit (binary, so one MB is
    /// 1048576 bytes and one KB is 1024 bytes).
    /// </summary>
    public long UnitBytes { get; }

    /// <summary>
    /// Gets the fixed display step in bytes. The minimum, default, and maximum
    /// are exact multiples of it.
    /// </summary>
    public long StepBytes { get; }

    /// <summary>
    /// Gets the fixed number of fractional display digits.
    /// </summary>
    public int DisplayPrecision { get; }

    /// <summary>
    /// Gets the validated minimum in bytes.
    /// </summary>
    public long MinimumBytes { get; }

    /// <summary>
    /// Gets the default in bytes.
    /// </summary>
    public long DefaultBytes { get; }

    /// <summary>
    /// Gets the validated maximum in bytes.
    /// </summary>
    public long MaximumBytes { get; }

    /// <summary>
    /// Formats one stored byte value in the field's fixed unit and precision,
    /// trimming trailing zeros and never switching units.
    /// </summary>
    /// <param name="bytes">The stored byte value.</param>
    /// <returns>The displayed value without the unit label.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or too large to render.</exception>
    public string FormatDisplay(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        var divisor = PowerOfTen(DisplayPrecision);
        if (bytes > (long.MaxValue - (UnitBytes / 2)) / divisor)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "The byte value is too large to display.");
        }

        var scaled = ((bytes * divisor) + (UnitBytes / 2)) / UnitBytes;
        return FormatScaled(scaled);
    }

    /// <summary>
    /// Formats the field's validated range in the field's fixed unit and
    /// precision. A range that spans the 1 MiB boundary stays in the field's
    /// unit and shows the sub-1-MiB bound as an exact fraction.
    /// </summary>
    /// <returns>The range label without the surrounding note text.</returns>
    public string FormatRange()
    {
        return FormatDisplay(MinimumBytes) + " - " + FormatDisplay(MaximumBytes) + " " + UnitLabel;
    }

    /// <summary>
    /// Converts a displayed value in the field's fixed unit back to bytes. A
    /// value with more fractional digits than the field's precision, or a value
    /// that does not land exactly on the field's step, is rejected rather than
    /// rounded.
    /// </summary>
    /// <param name="text">The displayed decimal value.</param>
    /// <param name="bytes">The exact byte value when the conversion succeeds.</param>
    /// <returns><see langword="true"/> when the value is an exact step multiple.</returns>
    public bool TryParseDisplay(string? text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var separator = trimmed.IndexOf('.', StringComparison.Ordinal);
        var integerPart = separator < 0 ? trimmed : trimmed[..separator];
        var fractionalPart = separator < 0 ? string.Empty : trimmed[(separator + 1)..];

        if (integerPart.Length == 0
            || (separator >= 0 && fractionalPart.Length == 0)
            || fractionalPart.Length > DisplayPrecision
            || fractionalPart.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var scaledText = integerPart + fractionalPart.PadRight(DisplayPrecision, '0');
        if (!long.TryParse(scaledText, NumberStyles.None, CultureInfo.InvariantCulture, out var scaled))
        {
            return false;
        }

        var divisor = PowerOfTen(DisplayPrecision);
        if (scaled > (long.MaxValue - (divisor / 2)) / UnitBytes)
        {
            return false;
        }

        var parsed = ((scaled * UnitBytes) + (divisor / 2)) / divisor;
        if (parsed % StepBytes != 0)
        {
            return false;
        }

        bytes = parsed;
        return true;
    }

    private static long PowerOfTen(int precision)
    {
        var value = 1L;
        for (var i = 0; i < precision; i++)
        {
            value *= 10;
        }

        return value;
    }

    private string FormatScaled(long scaled)
    {
        var divisor = PowerOfTen(DisplayPrecision);
        if (DisplayPrecision == 0)
        {
            return scaled.ToString(CultureInfo.InvariantCulture);
        }

        var text = (scaled / divisor).ToString(CultureInfo.InvariantCulture)
            + "."
            + (scaled % divisor).ToString(CultureInfo.InvariantCulture).PadLeft(DisplayPrecision, '0');
        return text.TrimEnd('0').TrimEnd('.');
    }
}
