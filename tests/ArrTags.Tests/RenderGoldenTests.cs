using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 golden-image tests. Each committed golden is a
/// repository-owned synthetic artifact produced by the pinned renderer; the test
/// renders the same request and compares decoded pixel planes, dimensions,
/// channel/alpha behavior, the encoded bytes, the output hash, and the output
/// fingerprint. There is no writer, auto-update, or auto-approval path: a changed
/// golden or a changed render fails the exact pixel comparison. These tests load
/// the native renderer and are environment-guarded.
/// </summary>
public class RenderGoldenTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    /// <summary>Gets the golden fixture names for the theory.</summary>
    public static IEnumerable<object[]> GoldenNames =>
        RenderGoldenFixtures.Names.Select(name => new object[] { name });

    [SkiaNativeTheory]
    [MemberData(nameof(GoldenNames))]
    public async Task GoldenMatchesDecodedPixelsBytesDimensionsAndFingerprint(string name)
    {
        var golden = GoldenStore.Load(name);
        var result = await Renderer.RenderAsync(RenderGoldenFixtures.BuildRequest(name), CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.True(result.HasArtifact);
        Assert.Equal(RenderResult.PngContentType, result.ContentType);
        Assert.Equal(golden.Entry.Width, result.Width);
        Assert.Equal(golden.Entry.Height, result.Height);
        Assert.Equal(golden.Entry.OutputHash, result.OutputHash);
        Assert.Equal(golden.Entry.OutputFingerprint, result.OutputFingerprint);

        var actualBytes = result.PngBytes.ToArray();
        Assert.Equal(golden.Bytes, actualBytes);
        Assert.Equal(golden.Entry.PngColorType, RenderImageFixtures.ReadPngColorType(actualBytes));

        var expectedPixels = RenderGoldenFixtures.DecodePixels(
            golden.Bytes,
            out var expectedWidth,
            out var expectedHeight,
            out var expectedChannels);
        var actualPixels = RenderGoldenFixtures.DecodePixels(
            actualBytes,
            out var actualWidth,
            out var actualHeight,
            out var actualChannels);

        Assert.Equal(expectedWidth, actualWidth);
        Assert.Equal(expectedHeight, actualHeight);
        Assert.Equal(expectedChannels, actualChannels);
        Assert.Equal(golden.Entry.Channels, actualChannels);

        var comparison = CrossRuntimePixelComparator.Compare(
            expectedPixels,
            actualPixels,
            actualWidth,
            actualHeight,
            actualChannels,
            PixelComparisonMode.Exact);
        Assert.True(comparison.IsMatch, comparison.Reason);

        AssertAlphaBehavior(actualChannels, actualPixels);
    }

    [Fact]
    public void CommittedGoldenManifestIsCompleteAndSelfConsistent()
    {
        var entries = GoldenManifestReader.ReadAll();
        Assert.Equal(RenderGoldenFixtures.Names.OrderBy(name => name, StringComparer.Ordinal), entries.Select(entry => entry.Name).OrderBy(name => name, StringComparer.Ordinal));

        foreach (var name in RenderGoldenFixtures.Names)
        {
            var entry = entries.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            var expected = RenderGoldenFixtures.Expected(name);
            Assert.Equal(expected.Width, entry.Width);
            Assert.Equal(expected.Height, entry.Height);
            Assert.Equal(expected.Channels, entry.Channels);
            Assert.Equal(expected.PngColorType, entry.PngColorType);

            var path = Path.Combine(GoldenStore.Directory, entry.PngFile);
            Assert.True(File.Exists(path), $"The committed golden '{entry.PngFile}' is missing.");
            var bytes = File.ReadAllBytes(path);

            // The committed bytes must match the recorded hash and the parsed
            // IHDR dimensions/color type independently of the renderer.
            Assert.Equal(entry.OutputHash, Convert.ToHexString(SHA256.HashData(bytes)));
            Assert.Equal(entry.Width, RendererBehaviorFixtures.ReadBigEndianInt32(bytes, 16));
            Assert.Equal(entry.Height, RendererBehaviorFixtures.ReadBigEndianInt32(bytes, 20));
            Assert.Equal(entry.PngColorType, RenderImageFixtures.ReadPngColorType(bytes));
            Assert.Equal(8, bytes[24]);
            Assert.Equal(0, bytes[28]);
        }
    }

    [Fact]
    public void AChangedGoldenPixelFailsTheExactGoldenComparison()
    {
        // The delivered golden test has no auto-approval path: mutating a single
        // channel of a decoded golden plane must fail the exact comparison that
        // the golden test uses.
        var plane = new byte[100 * 10 * 3];
        var mutated = new byte[plane.Length];
        Array.Copy(plane, mutated, plane.Length);
        mutated[0] = 1;

        var comparison = CrossRuntimePixelComparator.Compare(
            plane,
            mutated,
            100,
            10,
            3,
            PixelComparisonMode.Exact);

        Assert.False(comparison.IsMatch);
    }

    private static void AssertAlphaBehavior(int channels, byte[] pixels)
    {
        if (channels == 3)
        {
            return;
        }

        var transparent = 0;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 255)
            {
                transparent++;
            }
        }

        Assert.True(transparent > 0, "An RGBA golden must preserve meaningful source alpha.");
    }
}
