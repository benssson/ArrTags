namespace ArrTags.Rendering;

/// <summary>
/// The code-owned V1 output policy from ADR-009 and ADR-010. Every value here is
/// an output-affecting input to the render fingerprint. Format, color-space,
/// alpha model, font identity, palette, scale policy, and text limits are not
/// user-selectable except where ADR-010 exposes a contrast-validated palette
/// override; callers pass the effective policy for one render attempt.
/// </summary>
public sealed class RenderOutputPolicy
{
    /// <summary>
    /// Gets the default ADR-009 output policy. The font identity carries the
    /// pinned DejaVu Sans Bold 2.37 asset name; task 4.7 adds its SHA-256.
    /// </summary>
    public static RenderOutputPolicy Default { get; } = new RenderOutputPolicy();

    /// <summary>
    /// Gets the output format. V1 is a lossless 8-bit PNG.
    /// </summary>
    public string OutputFormat { get; init; } = "image/png";

    /// <summary>
    /// Gets the output pixel color space. V1 converts supported input profiles
    /// to sRGB.
    /// </summary>
    public string ColorSpace { get; init; } = "sRGB";

    /// <summary>
    /// Gets the alpha policy. V1 preserves meaningful source alpha and keeps
    /// badge pixels opaque.
    /// </summary>
    public string AlphaPolicy { get; init; } = "PreserveMeaningfulSourceAlpha";

    /// <summary>
    /// Gets the deterministic font asset identity. There is no font fallback.
    /// </summary>
    public string FontIdentity { get; init; } = "DejaVu Sans Bold 2.37";

    /// <summary>
    /// Gets the technical-badge background color.
    /// </summary>
    public string TechnicalBackground { get; init; } = "#111827";

    /// <summary>
    /// Gets the technical-badge text color.
    /// </summary>
    public string TechnicalText { get; init; } = "#FFFFFF";

    /// <summary>
    /// Gets the upgrade-status background color.
    /// </summary>
    public string StatusBackground { get; init; } = "#B45309";

    /// <summary>
    /// Gets the upgrade-status text color.
    /// </summary>
    public string StatusText { get; init; } = "#FFFFFF";

    /// <summary>
    /// Gets the reference poster width in pixels for the scale policy.
    /// </summary>
    public int ScaleReferenceWidth { get; init; } = 1000;

    /// <summary>
    /// Gets the minimum uniform scale applied to the reference geometry.
    /// </summary>
    public double MinimumScale { get; init; } = 0.5;

    /// <summary>
    /// Gets the maximum uniform scale applied to the reference geometry.
    /// </summary>
    public double MaximumScale { get; init; } = 4.0;

    /// <summary>
    /// Gets the maximum number of Unicode scalar values in a visible label.
    /// </summary>
    public int MaximumScalarValues { get; init; } = 24;

    /// <summary>
    /// Gets the number of leading Unicode scalar values retained before the
    /// truncation marker.
    /// </summary>
    public int RetainedPrefixScalarValues { get; init; } = 21;

    /// <summary>
    /// Gets the truncation marker appended to shortened labels.
    /// </summary>
    public string Ellipsis { get; init; } = "...";
}
