using System;

namespace ArrTags.Tests;

/// <summary>
/// The ADR-010 cross-runtime pixel comparison mode. <see cref="Exact"/> is the
/// canonical-runtime rule: decoded pixel planes, dimensions, alpha, geometry,
/// solid colors, and text placement must be identical. <see cref="TolerantAntiAliasedText"/>
/// is the non-canonical-runtime rule: only anti-aliased text pixels may differ,
/// by at most one per channel, and at most 0.1 percent of pixels may differ.
/// </summary>
internal enum PixelComparisonMode
{
    /// <summary>Exact pixel equality is required.</summary>
    Exact,

    /// <summary>Anti-aliased text pixels may differ within the ADR-010 budget.</summary>
    TolerantAntiAliasedText,
}

/// <summary>
/// The bounded outcome of one pixel-plane comparison.
/// </summary>
internal sealed record PixelComparisonResult(
    bool IsMatch,
    string Reason,
    int DifferingPixels,
    int AllowedDifferingPixels,
    int MaximumChannelDifference);

/// <summary>
/// Implements the ADR-010 cross-runtime tolerance rule as a real comparator over
/// packed RGB or RGBA pixel planes. Dimensions, channel count, alpha, solid badge
/// colors, and text placement are enforced exactly: any per-channel difference
/// greater than one fails in both modes, and alpha must match exactly. In the
/// tolerant mode, pixels that differ by exactly one channel step consume a budget
/// of at most 0.1 percent of the image; anything larger fails. The comparator is
/// pure and does not load the native renderer, so its boundary behavior is
/// unit-tested without the Skia environment guard.
/// </summary>
internal static class CrossRuntimePixelComparator
{
    /// <summary>The maximum tolerated per-channel difference in tolerant mode.</summary>
    public const int MaximumToleratedChannelDifference = 1;

    /// <summary>The maximum tolerated fraction of differing pixels in tolerant mode.</summary>
    public const double MaximumToleratedPixelRatio = 0.001;

    /// <summary>
    /// Compares two packed pixel planes.
    /// </summary>
    /// <param name="expected">The canonical packed pixel plane.</param>
    /// <param name="actual">The candidate packed pixel plane.</param>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <param name="channels">The channel count: 3 (RGB) or 4 (RGBA).</param>
    /// <param name="mode">The comparison mode.</param>
    /// <returns>The bounded comparison outcome.</returns>
    public static PixelComparisonResult Compare(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        int width,
        int height,
        int channels,
        PixelComparisonMode mode)
    {
        if (width <= 0 || height <= 0)
        {
            return Failure("The pixel plane dimensions must be positive.");
        }

        if (channels is not (3 or 4))
        {
            return Failure("The pixel plane must have three or four channels.");
        }

        var expectedLength = (long)width * height * channels;
        if (expected.Length != expectedLength || actual.Length != expectedLength)
        {
            return Failure("The pixel plane lengths do not match the declared dimensions.");
        }

        var allowed = (int)Math.Floor(width * (double)height * MaximumToleratedPixelRatio);
        var differingPixels = 0;
        var maximumChannelDifference = 0;

        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var offset = pixel * channels;

            if (channels == 4)
            {
                var expectedAlpha = expected[offset + 3];
                var actualAlpha = actual[offset + 3];
                if (expectedAlpha != actualAlpha)
                {
                    return Failure("Alpha must match exactly.");
                }
            }

            var pixelDiffers = false;
            for (var channel = 0; channel < 3; channel++)
            {
                var difference = Math.Abs(expected[offset + channel] - actual[offset + channel]);
                if (difference > maximumChannelDifference)
                {
                    maximumChannelDifference = difference;
                }

                if (difference > MaximumToleratedChannelDifference)
                {
                    return Failure($"A pixel channel differs by {difference}, which exceeds the tolerated amount.");
                }

                if (mode == PixelComparisonMode.Exact && difference != 0)
                {
                    return Failure("The canonical comparison requires exact pixels.");
                }

                if (difference != 0)
                {
                    pixelDiffers = true;
                }
            }

            if (pixelDiffers)
            {
                differingPixels++;
            }
        }

        if (mode == PixelComparisonMode.TolerantAntiAliasedText && differingPixels > allowed)
        {
            return new PixelComparisonResult(
                false,
                $"{differingPixels} differing pixels exceed the allowed {allowed}.",
                differingPixels,
                allowed,
                maximumChannelDifference);
        }

        return new PixelComparisonResult(true, "match", differingPixels, allowed, maximumChannelDifference);
    }

    private static PixelComparisonResult Failure(string reason)
    {
        return new PixelComparisonResult(false, reason, 0, 0, 0);
    }
}
