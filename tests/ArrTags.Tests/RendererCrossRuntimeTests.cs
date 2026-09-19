using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 cross-runtime validation. The canonical half renders the
/// committed golden fixtures and requires exact decoded pixel equality. The
/// tolerant half exercises the ADR-010 anti-aliased-text budget against a real
/// render. The explicitly supported non-canonical Linux runtime is not available
/// in this environment (only the pinned canonical runtime exists), so the
/// tolerant cross-runtime run is recorded as an environment limitation in the
/// task report and documentation; the comparator itself is unit-tested unguarded
/// in <see cref="CrossRuntimePixelComparatorTests"/>.
/// </summary>
public class RendererCrossRuntimeTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    /// <summary>Gets the golden fixture names for the canonical theory.</summary>
    public static IEnumerable<object[]> GoldenNames =>
        RenderGoldenFixtures.Names.Select(name => new object[] { name });

    [SkiaNativeTheory]
    [MemberData(nameof(GoldenNames))]
    public async Task CanonicalRuntimeMatchesCommittedGoldenExactly(string name)
    {
        var golden = GoldenStore.Load(name);
        var result = await Renderer.RenderAsync(RenderGoldenFixtures.BuildRequest(name), CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        var actualBytes = result.PngBytes.ToArray();
        var expectedPixels = RenderGoldenFixtures.DecodePixels(golden.Bytes, out var width, out var height, out var channels);
        var actualPixels = RenderGoldenFixtures.DecodePixels(actualBytes, out _, out _, out _);

        var comparison = CrossRuntimePixelComparator.Compare(
            expectedPixels,
            actualPixels,
            width,
            height,
            channels,
            PixelComparisonMode.Exact);

        Assert.True(comparison.IsMatch, comparison.Reason);
    }

    [SkiaNativeFact]
    public async Task TolerantComparatorAcceptsTheBudgetAndRejectsOnePixelMore()
    {
        var result = await Renderer.RenderAsync(RenderGoldenFixtures.BuildRequest("all-fields"), CancellationToken.None);
        Assert.Equal(RenderStatus.Rendered, result.Status);

        var expected = RenderGoldenFixtures.DecodePixels(result.PngBytes.ToArray(), out var width, out var height, out var channels);
        var allowed = (int)Math.Floor(width * (double)height * CrossRuntimePixelComparator.MaximumToleratedPixelRatio);
        Assert.True(allowed > 0);

        var withinBudget = (byte[])expected.Clone();
        var edgePixels = FindEdgePixels(expected, width, height, channels);
        for (var index = 0; index < allowed; index++)
        {
            Bump(withinBudget, edgePixels[index]);
        }

        var withinResult = CrossRuntimePixelComparator.Compare(
            expected,
            withinBudget,
            width,
            height,
            channels,
            PixelComparisonMode.TolerantAntiAliasedText);
        Assert.True(withinResult.IsMatch, withinResult.Reason);
        Assert.Equal(allowed, withinResult.DifferingPixels);

        var overBudget = (byte[])expected.Clone();
        for (var index = 0; index <= allowed; index++)
        {
            Bump(overBudget, edgePixels[index]);
        }

        var overResult = CrossRuntimePixelComparator.Compare(
            expected,
            overBudget,
            width,
            height,
            channels,
            PixelComparisonMode.TolerantAntiAliasedText);
        Assert.False(overResult.IsMatch);
    }

    [SkiaNativeFact]
    public async Task TolerantComparatorRejectsARealTwoStepPixelDifference()
    {
        var result = await Renderer.RenderAsync(RenderGoldenFixtures.BuildRequest("all-fields"), CancellationToken.None);
        Assert.Equal(RenderStatus.Rendered, result.Status);

        var expected = RenderGoldenFixtures.DecodePixels(result.PngBytes.ToArray(), out var width, out var height, out var channels);
        var changed = (byte[])expected.Clone();
        var edge = FindEdgePixels(expected, width, height, channels)[0];
        changed[edge] = (byte)Math.Min(255, changed[edge] + 2);

        var comparison = CrossRuntimePixelComparator.Compare(
            expected,
            changed,
            width,
            height,
            channels,
            PixelComparisonMode.TolerantAntiAliasedText);

        Assert.False(comparison.IsMatch);
    }

    [NonCanonicalRuntimeFact]
    public async Task SuppliedNonCanonicalGoldenSetMatchesWithinTheAdr010Tolerance()
    {
        // The comparison is data-driven: dropping a Goldens/non-canonical set
        // (manifest plus PNGs from a second, explicitly selected non-canonical
        // Linux runtime) into the test project makes this run with no code
        // change once ARRTAGS_NONCANONICAL_GOLDENS=1 is set. Until that runtime
        // is supplied the fact is reported as skipped rather than a fabricated
        // result; if it is enabled without the set the test fails.
        Assert.True(
            NonCanonicalGoldenStore.Exists,
            "ARRTAGS_NONCANONICAL_GOLDENS=1 was set but the Goldens/non-canonical set is missing.");

        foreach (var entry in NonCanonicalGoldenStore.ReadAll())
        {
            var golden = NonCanonicalGoldenStore.Load(entry.Name);
            var result = await Renderer.RenderAsync(
                RenderGoldenFixtures.BuildRequest(entry.Name),
                CancellationToken.None);
            Assert.Equal(RenderStatus.Rendered, result.Status);

            var expected = RenderGoldenFixtures.DecodePixels(golden.Bytes, out var width, out var height, out var channels);
            var actual = RenderGoldenFixtures.DecodePixels(result.PngBytes.ToArray(), out _, out _, out _);

            var comparison = CrossRuntimePixelComparator.Compare(
                expected,
                actual,
                width,
                height,
                channels,
                PixelComparisonMode.TolerantAntiAliasedText);
            Assert.True(comparison.IsMatch, $"{entry.Name}: {comparison.Reason}");
        }
    }

    private static int[] FindEdgePixels(byte[] pixels, int width, int height, int channels)
    {
        var edges = new List<int>();
        for (var y = 1; y < height - 1 && edges.Count < 100000; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = ((y * width) + x) * channels;
                var left = ((y * width) + x - 1) * channels;
                var right = ((y * width) + x + 1) * channels;
                if (pixels[index] != pixels[left] || pixels[index] != pixels[right])
                {
                    edges.Add(index);
                }
            }
        }

        if (edges.Count == 0)
        {
            edges.Add(0);
        }

        return edges.ToArray();
    }

    private static void Bump(byte[] pixels, int index)
    {
        pixels[index] = pixels[index] < 255 ? (byte)(pixels[index] + 1) : (byte)(pixels[index] - 1);
    }
}
