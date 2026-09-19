using System;

namespace ArrTags.Rendering;

/// <summary>
/// Validates the ADR-009 text/background contrast policy before any draw work.
/// Every configured style must reach a contrast ratio of at least
/// <see cref="MinimumRatio"/> against its own opaque background, because badge
/// backing is fully opaque and readability therefore does not depend on the
/// poster behind the pill. An unparseable color or a low ratio fails closed with
/// a safe reason.
/// </summary>
public static class BadgeContrast
{
    /// <summary>
    /// The minimum required text/background contrast ratio.
    /// </summary>
    public const double MinimumRatio = 4.5;

    /// <summary>
    /// Validates the technical and status style pairs in one output policy.
    /// </summary>
    /// <param name="policy">The effective output policy.</param>
    /// <returns><see langword="null"/> when every style is valid; otherwise the safe failure reason.</returns>
    /// <exception cref="ArgumentNullException">The policy is <see langword="null"/>.</exception>
    public static RenderFailureReason? Validate(RenderOutputPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var technical = ValidateStyle(
            policy.TechnicalText,
            policy.TechnicalBackground,
            out var technicalReason);
        if (!technical)
        {
            return technicalReason;
        }

        var status = ValidateStyle(
            policy.StatusText,
            policy.StatusBackground,
            out var statusReason);
        return status ? null : statusReason;
    }

    private static bool ValidateStyle(string text, string background, out RenderFailureReason reason)
    {
        if (!RgbColor.TryParse(text, out var foregroundColor)
            || !RgbColor.TryParse(background, out var backgroundColor))
        {
            reason = RenderFailureReason.ColorPolicyInvalid;
            return false;
        }

        if (RgbColor.ContrastRatio(foregroundColor, backgroundColor) < MinimumRatio)
        {
            reason = RenderFailureReason.ContrastTooLow;
            return false;
        }

        reason = RenderFailureReason.InvalidRequest;
        return true;
    }
}
