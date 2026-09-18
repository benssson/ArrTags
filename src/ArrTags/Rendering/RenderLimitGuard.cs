using System;
using ArrTags.Configuration;

namespace ArrTags.Rendering;

/// <summary>
/// Enforces the accepted ADR-009/ADR-010 image limits before decode, draw, or
/// encode work. It is a provider-neutral boundary: it consumes only primitive
/// descriptors (byte length and oriented dimensions) and the accepted
/// <see cref="OperationalLimits"/>, and it never opens a path, reads a provider,
/// or mutates source bytes. A rejected result carries one safe reason code and
/// no artifact.
/// </summary>
public static class RenderLimitGuard
{
    /// <summary>
    /// Validates a bounded source descriptor before any decode or allocation.
    /// The byte length is compared with the accepted source-artifact limit, and
    /// each oriented dimension is compared with the accepted per-side decoded
    /// dimension limit.
    /// </summary>
    /// <param name="sourceByteLength">The exact retained source byte length.</param>
    /// <param name="orientedWidth">The source width in pixels after orientation.</param>
    /// <param name="orientedHeight">The source height in pixels after orientation.</param>
    /// <param name="limits">The accepted operational limits.</param>
    /// <returns>An accepted result, or a rejected result with a safe reason.</returns>
    /// <exception cref="ArgumentNullException">The limits are <see langword="null"/>.</exception>
    public static RenderLimitResult ValidateSourceImage(
        long sourceByteLength,
        int orientedWidth,
        int orientedHeight,
        OperationalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (sourceByteLength <= 0 || orientedWidth <= 0 || orientedHeight <= 0)
        {
            return RenderLimitResult.Rejected(RenderLimitReason.MalformedSource);
        }

        if (sourceByteLength > limits.SourceArtifactLimitBytes)
        {
            return RenderLimitResult.Rejected(RenderLimitReason.SourceByteLimitExceeded);
        }

        if (ExceedsDimensionLimit(orientedWidth, orientedHeight, limits.MaxImageDimensionPixels))
        {
            return RenderLimitResult.Rejected(RenderLimitReason.SourceDimensionLimitExceeded);
        }

        return RenderLimitResult.Accepted;
    }

    /// <summary>
    /// Validates the planned output surface before any encode or buffer
    /// allocation. The output dimensions preserve the oriented source
    /// dimensions, so each side is compared with the per-side dimension limit
    /// and the uncompressed RGBA surface is compared with the accepted
    /// derived-artifact byte limit as a conservative pre-encode bound.
    /// </summary>
    /// <param name="orientedWidth">The planned output width in pixels.</param>
    /// <param name="orientedHeight">The planned output height in pixels.</param>
    /// <param name="limits">The accepted operational limits.</param>
    /// <returns>An accepted result, or a rejected result with a safe reason.</returns>
    /// <exception cref="ArgumentNullException">The limits are <see langword="null"/>.</exception>
    public static RenderLimitResult ValidateDerivedOutput(
        int orientedWidth,
        int orientedHeight,
        OperationalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (orientedWidth <= 0 || orientedHeight <= 0)
        {
            return RenderLimitResult.Rejected(RenderLimitReason.MalformedSource);
        }

        if (ExceedsDimensionLimit(orientedWidth, orientedHeight, limits.MaxImageDimensionPixels))
        {
            return RenderLimitResult.Rejected(RenderLimitReason.OutputDimensionLimitExceeded);
        }

        var pixels = (long)orientedWidth * orientedHeight;
        if (pixels > limits.DerivedArtifactLimitBytes / 4)
        {
            return RenderLimitResult.Rejected(RenderLimitReason.OutputByteLimitExceeded);
        }

        return RenderLimitResult.Accepted;
    }

    private static bool ExceedsDimensionLimit(int width, int height, int maximumDimensionPixels)
    {
        return width > maximumDimensionPixels || height > maximumDimensionPixels;
    }
}
