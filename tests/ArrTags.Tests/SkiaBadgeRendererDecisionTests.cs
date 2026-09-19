using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the renderer service decisions that are made
/// before any decode work: pass-through for missing, ineligible, or
/// non-displayable input; bounded failure for resource-limit violations; and
/// cancellation. These paths do not load the SkiaSharp native library.
/// </summary>
public class SkiaBadgeRendererDecisionTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [Fact]
    public async Task IneligibleSurfacePassesThrough()
    {
        var identity = new MediaIdentity(RenderTestFixtures.ItemId, MediaItemType.Series);
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            identity: identity,
            match: RenderTestFixtures.BuildMatch(identity),
            metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, result.Status);
        Assert.Equal(RenderPassThroughReason.IneligibleSurface, result.PassThroughReason);
    }

    [Fact]
    public async Task NonMatchedResultPassesThrough()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            identity: identity,
            match: RenderTestFixtures.BuildMatch(identity, MediaMatchStatus.NotFound),
            metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, result.Status);
        Assert.Equal(RenderPassThroughReason.MatchNotEligible, result.PassThroughReason);
    }

    [Fact]
    public async Task MissingMetadataPassesThrough()
    {
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: null);

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, result.Status);
        Assert.Equal(RenderPassThroughReason.NoMetadata, result.PassThroughReason);
    }

    [Fact]
    public async Task NoDisplayableValuePassesThrough()
    {
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: RenderTestFixtures.BuildEmptyMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, result.Status);
        Assert.Equal(RenderPassThroughReason.NoDisplayableValue, result.PassThroughReason);
    }

    [Fact]
    public async Task UnavailableSourcePassesThrough()
    {
        var request = RenderTestFixtures.BuildRequest(
            source: null,
            metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.PassThrough, result.Status);
        Assert.Equal(RenderPassThroughReason.SourceUnavailable, result.PassThroughReason);
    }

    [Fact]
    public async Task SourceByteLimitIsRejectedBeforeDecode()
    {
        var limits = new OperationalLimits { SourceArtifactLimitBytes = 2 };
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: RenderTestFixtures.BuildMetadata(),
            limits: limits);

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.SourceByteLimitExceeded, result.FailureReason);
        Assert.False(result.HasArtifact);
    }

    [Fact]
    public async Task SourceDimensionLimitIsRejectedBeforeDecode()
    {
        var limits = new OperationalLimits { MaxImageDimensionPixels = 50 };
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: RenderTestFixtures.BuildMetadata(),
            limits: limits);

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.SourceDimensionLimitExceeded, result.FailureReason);
    }

    [Fact]
    public async Task DerivedOutputLimitIsRejectedBeforeDecode()
    {
        var limits = new OperationalLimits { DerivedArtifactLimitBytes = 100 };
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: RenderTestFixtures.BuildMetadata(),
            limits: limits);

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.OutputByteLimitExceeded, result.FailureReason);
    }

    [Fact]
    public async Task LowContrastPolicyFailsClosedBeforeDecode()
    {
        var policy = new RenderOutputPolicy
        {
            TechnicalText = "#222222",
        };
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: RenderTestFixtures.BuildMetadata(),
            policy: policy);

        var result = await Renderer.RenderAsync(request, CancellationToken.None);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.ContrastTooLow, result.FailureReason);
    }

    [Fact]
    public async Task CancelledTokenReturnsBoundedFailure()
    {
        var request = RenderTestFixtures.BuildRequest(
            Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, new CancellationToken(canceled: true));

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.Cancelled, result.FailureReason);
        Assert.False(result.HasArtifact);
    }

    [Fact]
    public async Task NullRequestIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Renderer.RenderAsync(null!, CancellationToken.None));
    }

    private static SourceImageInput Source(byte[] bytes, int width, int height)
    {
        return new SourceImageInput(
            bytes,
            "image/png",
            width,
            height,
            SourceImageInput.ComputeSha256(bytes));
    }
}
