using System;
using System.Collections.Generic;

namespace ArrTags.Configuration;

/// <summary>
/// The ADR-028 code-owned display mapping for the seven byte-denominated
/// operational limits. The unit, step, and display precision are fixed per field
/// in code, and the persisted XML and <see cref="OperationalLimits"/> values stay
/// bytes. The dashboard settings page mirrors this mapping (unit, unit bytes,
/// step, and precision) for its display/store conversion, and its structural
/// tests compare the page against these definitions.
/// </summary>
public static class ByteLimitUnits
{
    private const long Kibibyte = 1024L;
    private const long Mebibyte = 1024L * Kibibyte;
    private const long Gibibyte = 1024L * Mebibyte;
    private const int FractionalDigits = 4;

    private static readonly IReadOnlyList<ByteLimitField> FieldsValue = new ByteLimitField[]
    {
        new ByteLimitField(
            nameof(OperationalLimits.ProviderResponseLimitBytes),
            "MB",
            Mebibyte,
            64L * Kibibyte,
            FractionalDigits,
            64L * Kibibyte,
            8L * Mebibyte,
            64L * Mebibyte),
        new ByteLimitField(
            nameof(OperationalLimits.WebhookMaxPayloadBytes),
            "KB",
            Kibibyte,
            Kibibyte,
            0,
            4L * Kibibyte,
            256L * Kibibyte,
            4L * Mebibyte),
        new ByteLimitField(
            nameof(OperationalLimits.SourceArtifactLimitBytes),
            "MB",
            Mebibyte,
            64L * Kibibyte,
            FractionalDigits,
            64L * Kibibyte,
            32L * Mebibyte,
            128L * Mebibyte),
        new ByteLimitField(
            nameof(OperationalLimits.DerivedArtifactLimitBytes),
            "MB",
            Mebibyte,
            64L * Kibibyte,
            FractionalDigits,
            64L * Kibibyte,
            32L * Mebibyte,
            128L * Mebibyte),
        new ByteLimitField(
            nameof(OperationalLimits.RenderCacheQuotaBytes),
            "MB",
            Mebibyte,
            Mebibyte,
            0,
            64L * Mebibyte,
            Gibibyte,
            64L * Gibibyte),
        new ByteLimitField(
            nameof(OperationalLimits.ArtifactStorageQuotaBytes),
            "MB",
            Mebibyte,
            Mebibyte,
            0,
            256L * Mebibyte,
            4L * Gibibyte,
            256L * Gibibyte),
        new ByteLimitField(
            nameof(OperationalLimits.InventoryCacheMaxBytes),
            "MB",
            Mebibyte,
            Mebibyte,
            0,
            Mebibyte,
            32L * Mebibyte,
            256L * Mebibyte),
    };

    /// <summary>
    /// Gets the seven byte-denominated limits in their configuration and
    /// settings-page order.
    /// </summary>
    public static IReadOnlyList<ByteLimitField> All => FieldsValue;

    /// <summary>
    /// Gets the display mapping for one operational-limit property.
    /// </summary>
    /// <param name="propertyName">The <see cref="OperationalLimits"/> property name.</param>
    /// <returns>The field definition.</returns>
    /// <exception cref="ArgumentException">The name is empty or is not a mapped byte limit.</exception>
    public static ByteLimitField Get(string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        foreach (var field in FieldsValue)
        {
            if (string.Equals(field.PropertyName, propertyName, StringComparison.Ordinal))
            {
                return field;
            }
        }

        throw new ArgumentException($"'{propertyName}' is not a byte-denominated operational limit.", nameof(propertyName));
    }
}
