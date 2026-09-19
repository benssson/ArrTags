using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.6 failure and pass-through behavior. A request with no
/// badge metadata, no displayable value, an ineligible surface, a non-matched
/// result, or no available source passes through and leaves the current artwork
/// unchanged. A structurally unusable, over-limit, low-contrast, invalid-color,
/// missing-font, malformed, or unsupported request fails with exactly one safe
/// reason code, never a partial PNG, and never a mutated source. The pre-decode
/// decisions are unguarded; the decode-dependent malformed and unsupported cases
/// are environment-guarded.
/// </summary>
public class RendererBehaviorFailureTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [Fact]
    public async Task IneligibleSeriesAndSeasonSurfacesPassThrough()
    {
        foreach (var itemType in new[] { MediaItemType.Series, MediaItemType.Season })
        {
            var identity = new MediaIdentity(RenderTestFixtures.ItemId, itemType);
            var request = RenderTestFixtures.BuildRequest(
                RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
                identity: identity,
                match: RenderTestFixtures.BuildMatch(identity),
                metadata: RendererBehaviorFixtures.BuildMetadata());

            await AssertPassThroughAsync(request, RenderPassThroughReason.IneligibleSurface);
        }
    }

    [Fact]
    public async Task NonMatchedResultPassesThrough()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var request = RenderTestFixtures.BuildRequest(
            RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            identity: identity,
            match: RenderTestFixtures.BuildMatch(identity, ArrTags.Matching.MediaMatchStatus.Ambiguous),
            metadata: RendererBehaviorFixtures.BuildMetadata());

        await AssertPassThroughAsync(request, RenderPassThroughReason.MatchNotEligible);
    }

    [Fact]
    public async Task MissingMetadataPassesThrough()
    {
        var request = RenderTestFixtures.BuildRequest(
            RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: null);

        await AssertPassThroughAsync(request, RenderPassThroughReason.NoMetadata);
    }

    [Fact]
    public async Task MetadataWithoutADisplayableValuePassesThrough()
    {
        var request = RenderTestFixtures.BuildRequest(
            RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: RenderTestFixtures.BuildEmptyMetadata());

        await AssertPassThroughAsync(request, RenderPassThroughReason.NoDisplayableValue);
    }

    [Fact]
    public async Task UnavailableSourcePassesThrough()
    {
        var request = RenderTestFixtures.BuildRequest(
            source: null,
            metadata: RendererBehaviorFixtures.BuildMetadata());

        await AssertPassThroughAsync(request, RenderPassThroughReason.SourceUnavailable);
    }

    [Fact]
    public async Task SourceByteLimitViolationFailsBounded()
    {
        var source = RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100);
        var request = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            limits: new OperationalLimits { SourceArtifactLimitBytes = 2 });

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.SourceByteLimitExceeded);
    }

    [Fact]
    public async Task SourceDimensionLimitViolationFailsBounded()
    {
        var source = RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100);
        var request = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            limits: new OperationalLimits { MaxImageDimensionPixels = 50 });

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.SourceDimensionLimitExceeded);
    }

    [Fact]
    public async Task DerivedOutputLimitViolationFailsBounded()
    {
        var source = RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100);
        var request = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            limits: new OperationalLimits { DerivedArtifactLimitBytes = 100 });

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.OutputByteLimitExceeded);
    }

    [Fact]
    public async Task LowContrastPolicyFailsBounded()
    {
        var source = RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100);
        var request = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            policy: new RenderOutputPolicy { TechnicalText = "#222222" });

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.ContrastTooLow);
    }

    [Fact]
    public async Task InvalidColorPolicyFailsBounded()
    {
        var source = RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100);
        var request = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            policy: new RenderOutputPolicy { TechnicalText = "#GGGGGG" });

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.ColorPolicyInvalid);
    }

    [Fact]
    public async Task MissingFontResourceFailsBounded()
    {
        var source = RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100);
        var policy = new RenderOutputPolicy
        {
            FontIdentity = new RenderFontIdentity(
                "Missing",
                "Bold",
                "1.0",
                1024,
                new string('A', 64),
                "ArrTags.Resources.DoesNotExist.ttf"),
        };
        var request = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            policy: policy);

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.FontUnavailable);
    }

    [SkiaNativeFact]
    public async Task UnsupportedSourceBytesFailBounded()
    {
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        var source = RendererBehaviorFixtures.Source(bytes, 100, 100);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RendererBehaviorFixtures.BuildMetadata());

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.UnsupportedInput);
    }

    [SkiaNativeFact]
    public async Task DescriptorThatDoesNotMatchTheDecodedDimensionsFailsAsMalformed()
    {
        var bytes = RendererBehaviorFixtures.CreateOpaquePng(20, 30).Bytes.ToArray();
        var source = RendererBehaviorFixtures.Source(bytes, 100, 100);
        var request = RenderTestFixtures.BuildRequest(source, metadata: RendererBehaviorFixtures.BuildMetadata());

        await AssertBoundedFailureAsync(request, source, RenderFailureReason.MalformedSource);
    }

    private static async Task AssertPassThroughAsync(RenderRequest request, RenderPassThroughReason expected)
    {
        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, result.Status);
        Assert.Equal(expected, result.PassThroughReason);
        Assert.Null(result.FailureReason);
        Assert.False(result.HasArtifact);
        Assert.True(result.PngBytes.IsEmpty);
        Assert.Null(result.ContentType);
        Assert.Null(result.OutputHash);
        Assert.Null(result.OutputFingerprint);
    }

    private static async Task AssertBoundedFailureAsync(
        RenderRequest request,
        SourceImageInput source,
        RenderFailureReason expected)
    {
        var before = source.Bytes.ToArray();

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(expected, result.FailureReason);
        Assert.Null(result.PassThroughReason);
        Assert.False(result.HasArtifact);
        Assert.True(result.PngBytes.IsEmpty);
        Assert.Null(result.ContentType);
        Assert.Null(result.OutputHash);
        Assert.Null(result.OutputFingerprint);
        Assert.Equal(before, source.Bytes.ToArray());
    }
}
