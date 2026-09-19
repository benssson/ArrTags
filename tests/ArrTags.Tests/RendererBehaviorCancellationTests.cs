using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.6 cancellation behavior. The renderer observes the caller token
/// before decode, after decode, between layout and drawing, and before
/// finalization (ADR-010). The render itself is a single synchronous bounded call
/// with no injectable mid-render hook, so the earliest checkpoint is the one that
/// can be deterministically exercised through the public contract; these
/// tests prove that checkpoint is reached before any input validation, surface
/// decision, limit check, decode, or draw, and that cancellation yields a bounded
/// failed result with no partial artifact and an unchanged source. The later
/// checkpoints are covered by inspection in the task 4.9 review and are not
/// independently reachable without production-only test hooks, which are out of
/// scope for a test task.
/// </summary>
public class RendererBehaviorCancellationTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [Fact]
    public async Task CancellationIsObservedBeforeDecodeWithNoArtifactAndAnUnchangedSource()
    {
        // The bytes are not a decodable image, so an uncancelled render would fail
        // later; a cancelled token must win before any decode attempt.
        var source = RendererBehaviorFixtures.Source(Encoding.ASCII.GetBytes("this is not an image"), 100, 100);
        var before = source.Bytes.ToArray();
        var request = RenderTestFixtures.BuildRequest(source, metadata: RenderTestFixtures.BuildMetadata());

        var result = await Renderer.RenderAsync(request, new CancellationToken(canceled: true));

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.Equal(RenderFailureReason.Cancelled, result.FailureReason);
        Assert.False(result.HasArtifact);
        Assert.True(result.PngBytes.IsEmpty);
        Assert.Null(result.ContentType);
        Assert.Null(result.OutputHash);
        Assert.Null(result.OutputFingerprint);
        Assert.Null(result.PassThroughReason);
        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
        Assert.Equal(before, source.Bytes.ToArray());
    }

    [Fact]
    public async Task CancellationPrecedesTheIneligibleSurfaceDecision()
    {
        var identity = new MediaIdentity(RenderTestFixtures.ItemId, MediaItemType.Series);
        var request = RenderTestFixtures.BuildRequest(
            RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100),
            identity: identity,
            match: RenderTestFixtures.BuildMatch(identity),
            metadata: RendererBehaviorFixtures.BuildMetadata());

        var uncancelled = await Renderer.RenderAsync(request, CancellationToken.None);
        var cancelled = await Renderer.RenderAsync(request, new CancellationToken(canceled: true));

        Assert.Equal(RenderPassThroughReason.IneligibleSurface, uncancelled.PassThroughReason);
        Assert.Equal(RenderFailureReason.Cancelled, cancelled.FailureReason);
    }

    [Fact]
    public async Task CancellationPrecedesTheLimitAndContrastDecisions()
    {
        var source = RendererBehaviorFixtures.Source(new byte[] { 1, 2, 3, 4 }, 100, 100);
        var limitRequest = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            limits: new OperationalLimits { SourceArtifactLimitBytes = 2 });
        var contrastRequest = RenderTestFixtures.BuildRequest(
            source,
            metadata: RendererBehaviorFixtures.BuildMetadata(),
            policy: new RenderOutputPolicy { TechnicalText = "#222222" });

        var limitUncancelled = await Renderer.RenderAsync(limitRequest, CancellationToken.None);
        var limitCancelled = await Renderer.RenderAsync(limitRequest, new CancellationToken(canceled: true));
        var contrastUncancelled = await Renderer.RenderAsync(contrastRequest, CancellationToken.None);
        var contrastCancelled = await Renderer.RenderAsync(contrastRequest, new CancellationToken(canceled: true));

        Assert.Equal(RenderFailureReason.SourceByteLimitExceeded, limitUncancelled.FailureReason);
        Assert.Equal(RenderFailureReason.Cancelled, limitCancelled.FailureReason);
        Assert.Equal(RenderFailureReason.ContrastTooLow, contrastUncancelled.FailureReason);
        Assert.Equal(RenderFailureReason.Cancelled, contrastCancelled.FailureReason);
    }

    [Fact]
    public async Task CancellationPrecedesEveryPassThroughDecisionEvenWithNoSourceOrMetadata()
    {
        var request = RenderTestFixtures.BuildRequest(source: null, metadata: null);

        var uncancelled = await Renderer.RenderAsync(request, CancellationToken.None);
        var cancelled = await Renderer.RenderAsync(request, new CancellationToken(canceled: true));

        Assert.Equal(RenderPassThroughReason.NoMetadata, uncancelled.PassThroughReason);
        Assert.Equal(RenderFailureReason.Cancelled, cancelled.FailureReason);
    }
}
