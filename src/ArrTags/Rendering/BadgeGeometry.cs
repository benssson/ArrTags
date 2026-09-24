using System;

namespace ArrTags.Rendering;

/// <summary>
/// The code-owned ADR-009 reference geometry and uniform scale policy. Every
/// value is defined at the <see cref="ReferenceWidth"/>-pixel poster width and
/// scaled by <c>clamp(width / referenceWidth, minimumScale, maximumScale)</c>.
/// Geometry is calculated in output pixels and there is no device-pixel-ratio,
/// <c>@2x</c>, or DPI branch. A geometry change is an output-affecting change and
/// therefore a renderer-version change.
/// </summary>
public static class BadgeGeometry
{
    /// <summary>
    /// The reference poster width in pixels.
    /// </summary>
    public const double ReferenceWidth = 1000.0;

    /// <summary>
    /// The reference outer inset from every poster edge in pixels.
    /// </summary>
    public const double OuterInset = 24.0;

    /// <summary>
    /// The reference gap between pills in one row in pixels.
    /// </summary>
    public const double PillGap = 8.0;

    /// <summary>
    /// The reference gap between the two rail rows in pixels.
    /// </summary>
    public const double RowGap = 8.0;

    /// <summary>
    /// The reference pill height in pixels.
    /// </summary>
    public const double PillHeight = 48.0;

    /// <summary>
    /// The reference pill corner radius in pixels.
    /// </summary>
    public const double CornerRadius = 8.0;

    /// <summary>
    /// The reference horizontal padding inside a pill in pixels.
    /// </summary>
    public const double HorizontalPadding = 12.0;

    /// <summary>
    /// The reference vertical padding inside a pill in pixels.
    /// </summary>
    public const double VerticalPadding = 7.0;

    /// <summary>
    /// The reference single-line font size in pixels.
    /// </summary>
    public const double FontSize = 28.0;

    /// <summary>
    /// The maximum number of technical rail rows.
    /// </summary>
    public const int MaximumRows = 2;

    /// <summary>
    /// The maximum number of pills in one technical rail row.
    /// </summary>
    public const int MaximumPillsPerRow = 3;

    /// <summary>
    /// The code-owned geometry factor for <see cref="BadgeSize.Small"/>.
    /// </summary>
    public const double SmallSizeFactor = 0.75;

    /// <summary>
    /// The code-owned geometry factor for <see cref="BadgeSize.Medium"/>. It is
    /// the V1 reference factor.
    /// </summary>
    public const double MediumSizeFactor = 1.0;

    /// <summary>
    /// The code-owned geometry factor for <see cref="BadgeSize.Large"/>.
    /// </summary>
    public const double LargeSizeFactor = 1.5;

    /// <summary>
    /// Returns the code-owned geometry factor for one preset badge size.
    /// </summary>
    /// <param name="size">The preset badge size.</param>
    /// <returns>The factor that multiplies the width-based scale.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size is not defined.</exception>
    public static double SizeFactor(BadgeSize size)
    {
        return size switch
        {
            BadgeSize.Small => SmallSizeFactor,
            BadgeSize.Medium => MediumSizeFactor,
            BadgeSize.Large => LargeSizeFactor,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown badge size."),
        };
    }

    /// <summary>
    /// Computes the uniform scale for one output width. The output preserves the
    /// oriented source dimensions, so the width alone selects the scale.
    /// </summary>
    /// <param name="outputWidth">The oriented output width in pixels.</param>
    /// <param name="policy">The effective output policy that owns the scale bounds.</param>
    /// <returns>The uniform geometry scale.</returns>
    /// <exception cref="ArgumentNullException">The policy is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The output width or reference width is not positive.</exception>
    /// <exception cref="ArgumentException">The policy scale bounds are not usable.</exception>
    public static double ComputeScale(int outputWidth, RenderOutputPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);

        if (policy.ScaleReferenceWidth <= 0)
        {
            throw new ArgumentException("The scale reference width must be positive.", nameof(policy));
        }

        if (policy.MinimumScale <= 0
            || policy.MaximumScale < policy.MinimumScale
            || double.IsNaN(policy.MinimumScale)
            || double.IsNaN(policy.MaximumScale))
        {
            throw new ArgumentException("The policy scale bounds are not usable.", nameof(policy));
        }

        var raw = outputWidth / (double)policy.ScaleReferenceWidth;
        return Math.Clamp(raw, policy.MinimumScale, policy.MaximumScale);
    }

    /// <summary>
    /// Computes the effective uniform scale for one output surface: the
    /// width-based <see cref="ComputeScale"/> multiplied by the policy's preset
    /// <see cref="BadgeSize"/> factor, then clamped so the scaled outer inset
    /// leaves a positive safe area (ADR-019 clauses 3 and 5). The layout engine's
    /// fit checks then shorten or omit any pill that still cannot fit rather than
    /// letting it paint outside the safe area. The default (<see cref="BadgeSize.Medium"/>)
    /// is identity-neutral, so it reproduces the V1 geometry exactly.
    /// </summary>
    /// <param name="outputWidth">The oriented output width in pixels.</param>
    /// <param name="outputHeight">The oriented output height in pixels.</param>
    /// <param name="policy">The effective output policy that owns the size and scale bounds.</param>
    /// <returns>The effective uniform geometry scale.</returns>
    /// <exception cref="ArgumentNullException">The policy is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An output dimension is not positive, or the size is not defined.</exception>
    /// <exception cref="ArgumentException">The policy scale bounds are not usable.</exception>
    public static double ComputeEffectiveScale(int outputWidth, int outputHeight, RenderOutputPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputHeight);

        var scale = ComputeScale(outputWidth, policy) * SizeFactor(policy.Size);

        // The scaled outer inset must not consume the whole poster, so the safe
        // area stays positive in both dimensions. A pill that still cannot fit
        // the safe area is shortened or omitted by the layout engine.
        var maximumInsetScale = Math.Min(outputWidth, outputHeight) / (2.0 * OuterInset);
        if (maximumInsetScale > 0 && scale > maximumInsetScale)
        {
            scale = maximumInsetScale;
        }

        return scale;
    }
}
