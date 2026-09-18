using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ArrTags.Rendering;

/// <summary>
/// Computes the deterministic render fingerprints from the canonical
/// output-affecting input snapshot. The result/output fingerprint is the logical
/// identity of the produced image and is independent of the Jellyfin item it is
/// attached to. The request fingerprint is the render key: it scopes the result
/// fingerprint to one item and poster surface. Both are SHA-256 hex strings over
/// the renderer version, the badge schema version, and every output-affecting
/// value; request correlation identifiers and timestamps are intentionally
/// absent, so equal inputs always produce equal fingerprints.
/// </summary>
public static class RenderFingerprint
{
    /// <summary>
    /// Computes the result/output fingerprint over the source, metadata,
    /// configuration, resolved badge values, output policy, and renderer and
    /// badge schema versions. It is independent of item identity.
    /// </summary>
    /// <param name="input">The output-affecting input snapshot.</param>
    /// <returns>The uppercase SHA-256 output fingerprint.</returns>
    /// <exception cref="ArgumentNullException">The input is <see langword="null"/>.</exception>
    public static string ComputeOutputFingerprint(RenderFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var policy = input.OutputPolicy;
        var builder = new StringBuilder();
        Append(builder, "rendererVersion", input.RendererVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, "badgeSchemaVersion", input.BadgeSchemaVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, "sourceImageFingerprint", input.SourceImageFingerprint);
        Append(builder, "sourceWidth", input.SourceWidth.ToString(CultureInfo.InvariantCulture));
        Append(builder, "sourceHeight", input.SourceHeight.ToString(CultureInfo.InvariantCulture));
        Append(builder, "metadataFingerprint", input.MetadataFingerprint);
        Append(builder, "configurationFingerprint", input.ConfigurationFingerprint);
        Append(builder, "technicalValues", DescribeTechnicalValues(input.Selection));
        Append(builder, "statusValue", DescribeStatusValue(input.Selection));
        Append(builder, "outputFormat", policy.OutputFormat);
        Append(builder, "colorSpace", policy.ColorSpace);
        Append(builder, "alphaPolicy", policy.AlphaPolicy);
        Append(builder, "fontIdentity", policy.FontIdentity.Descriptor);
        Append(builder, "paletteTechnicalBackground", policy.TechnicalBackground);
        Append(builder, "paletteTechnicalText", policy.TechnicalText);
        Append(builder, "paletteStatusBackground", policy.StatusBackground);
        Append(builder, "paletteStatusText", policy.StatusText);
        Append(builder, "scaleReferenceWidth", policy.ScaleReferenceWidth.ToString(CultureInfo.InvariantCulture));
        Append(builder, "scaleMinimum", policy.MinimumScale.ToString("R", CultureInfo.InvariantCulture));
        Append(builder, "scaleMaximum", policy.MaximumScale.ToString("R", CultureInfo.InvariantCulture));
        Append(builder, "textMaximumScalarValues", policy.MaximumScalarValues.ToString(CultureInfo.InvariantCulture));
        Append(builder, "textRetainedPrefixScalarValues", policy.RetainedPrefixScalarValues.ToString(CultureInfo.InvariantCulture));
        Append(builder, "textEllipsis", policy.Ellipsis);

        return Hash(builder);
    }

    /// <summary>
    /// Computes the request fingerprint (render key) from the result/output
    /// fingerprint plus the Jellyfin item and its poster surface. The same
    /// visual inputs on different items intentionally produce different request
    /// fingerprints and the same result/output fingerprint.
    /// </summary>
    /// <param name="input">The output-affecting input snapshot.</param>
    /// <returns>The uppercase SHA-256 request fingerprint.</returns>
    /// <exception cref="ArgumentNullException">The input is <see langword="null"/>.</exception>
    public static string ComputeRequestFingerprint(RenderFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var builder = new StringBuilder();
        Append(builder, "outputFingerprint", ComputeOutputFingerprint(input));
        Append(builder, "jellyfinItemId", input.JellyfinItemId.ToString("N", CultureInfo.InvariantCulture));
        Append(builder, "itemType", input.ItemType.ToString());

        return Hash(builder);
    }

    private static string Hash(StringBuilder builder)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append('=').Append(value ?? string.Empty).Append('\n');
    }

    private static string DescribeTechnicalValues(BadgeSelection selection)
    {
        if (selection.TechnicalValues.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < selection.TechnicalValues.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(';');
            }

            var value = selection.TechnicalValues[index];
            builder.Append(value.Selector.ToString()).Append(':').Append(Escape(value.Text));
        }

        return builder.ToString();
    }

    private static string DescribeStatusValue(BadgeSelection selection)
    {
        var status = selection.StatusValue;
        return status is null
            ? string.Empty
            : status.Selector.ToString() + ":" + Escape(status.Text);
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
