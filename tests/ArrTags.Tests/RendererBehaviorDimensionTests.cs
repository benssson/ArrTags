using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using SkiaSharp;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.6 dimension behavior. The renderer preserves the oriented
/// source pixel dimensions and aspect ratio for opaque and alpha sources, never
/// upscales or downscales a source to the 1000-pixel reference width, and applies
/// the ADR-009 geometry scale to badge geometry only. The geometry-scale checks
/// are pure; the real decode/encode dimension checks are environment-guarded.
/// </summary>
public class RendererBehaviorDimensionTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [Theory]
    [InlineData(320, 0.5)]
    [InlineData(500, 0.5)]
    [InlineData(1000, 1.0)]
    [InlineData(2000, 2.0)]
    [InlineData(5000, 4.0)]
    public void GeometryScaleIsAppliedUniformlyWithoutAnOutputSizeBranch(int width, double expectedScale)
    {
        var layout = BadgeLayoutEngine.Build(
            new[] { new BadgeValue(BadgeSelector.Quality, "1080p") },
            null,
            width,
            1500,
            RenderOutputPolicy.Default,
            text => text.Length * 10f);

        Assert.Equal(expectedScale, layout.Scale, 6);
        var pill = Assert.Single(layout.TechnicalPills);
        Assert.Equal(BadgeGeometry.PillHeight * expectedScale, pill.Height, 3);
        Assert.Equal(BadgeGeometry.OuterInset * expectedScale, pill.X, 3);
    }

    [SkiaNativeFact]
    public async Task SmallOpaqueSourceIsNotUpscaledAndKeepsItsAspectRatio()
    {
        var source = RendererBehaviorFixtures.CreateOpaquePng(320, 480);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(320, result.Width);
        Assert.Equal(480, result.Height);
        var header = RendererBehaviorFixtures.ReadIhdrDimensions(result.PngBytes.ToArray());
        Assert.Equal((320, 480), header);
    }

    [SkiaNativeFact]
    public async Task LargeOpaqueSourceIsNotDownscaledAndKeepsItsAspectRatio()
    {
        var source = RendererBehaviorFixtures.CreateOpaquePng(2000, 3000);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(2000, result.Width);
        Assert.Equal(3000, result.Height);
        var header = RendererBehaviorFixtures.ReadIhdrDimensions(result.PngBytes.ToArray());
        Assert.Equal((2000, 3000), header);
    }

    [SkiaNativeFact]
    public async Task AlphaSourceDimensionsArePreserved()
    {
        var source = RendererBehaviorFixtures.CreateAlphaPng(720, 480);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(720, result.Width);
        Assert.Equal(480, result.Height);
        var header = RendererBehaviorFixtures.ReadIhdrDimensions(result.PngBytes.ToArray());
        Assert.Equal((720, 480), header);
    }

    [SkiaNativeFact]
    public async Task AxisAlignedOrientationKeepsTheSourceDimensions()
    {
        // EXIF orientation 3 is a 180-degree rotation and does not swap axes.
        var bytes = RendererBehaviorFixtures.CreateOrientedJpeg(600, 900, orientation: 3);
        var source = RendererBehaviorFixtures.Source(bytes, 600, 900);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(600, result.Width);
        Assert.Equal(900, result.Height);
        using var decoded = SKBitmap.Decode(result.PngBytes.ToArray());
        Assert.NotNull(decoded);
        Assert.Equal(600, decoded.Width);
        Assert.Equal(900, decoded.Height);
    }

    [SkiaNativeFact]
    public async Task TransposingOrientationSwapsTheEncodedDimensions()
    {
        // EXIF orientation 8 is a 270-degree rotation and swaps width and height.
        var bytes = RendererBehaviorFixtures.CreateOrientedJpeg(600, 900, orientation: 8);
        var source = RendererBehaviorFixtures.Source(bytes, 900, 600);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.Equal(900, result.Width);
        Assert.Equal(600, result.Height);
        var header = RendererBehaviorFixtures.ReadIhdrDimensions(result.PngBytes.ToArray());
        Assert.Equal((900, 600), header);
    }
}
