using System;
using ArrTags.Configuration;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the immutable source descriptor and render request
/// guards. Required constructor arguments are validated in the repository's
/// existing guard style, and the request owns independent policy and limit
/// snapshots.
/// </summary>
public class RenderRequestTests
{
    private static readonly byte[] SampleBytes = { 1, 2, 3, 4 };

    [Fact]
    public void SourceDescriptorCarriesVerifiedIdentity()
    {
        var source = new SourceImageInput(
            SampleBytes,
            "image/png",
            600,
            900,
            SourceImageInput.ComputeSha256(SampleBytes));

        Assert.Equal(SampleBytes, source.Bytes.ToArray());
        Assert.Equal("image/png", source.ContentType);
        Assert.Equal(600, source.OrientedWidth);
        Assert.Equal(900, source.OrientedHeight);
        Assert.Equal(SourceImageInput.ComputeSha256(SampleBytes), source.SourceSha256);
    }

    [Fact]
    public void SourceDescriptorRejectsMalformedInput()
    {
        var hash = SourceImageInput.ComputeSha256(SampleBytes);

        Assert.Throws<ArgumentException>(
            () => new SourceImageInput(ReadOnlySpan<byte>.Empty, "image/png", 1, 1, hash));
        Assert.Throws<ArgumentException>(
            () => new SourceImageInput(SampleBytes, "text/plain", 1, 1, SourceImageInput.ComputeSha256(SampleBytes)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SourceImageInput(SampleBytes, "image/png", 0, 1, hash));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SourceImageInput(SampleBytes, "image/png", 1, -1, hash));
        Assert.Throws<ArgumentException>(
            () => new SourceImageInput(SampleBytes, "image/png", 1, 1, "ABCD"));
        Assert.Throws<ArgumentException>(
            () => new SourceImageInput(SampleBytes, "image/png", 1, 1, new string('0', 64)));
    }

    [Fact]
    public void RequestOwnsIndependentLimitSnapshot()
    {
        var limits = new OperationalLimits { SourceArtifactLimitBytes = 1234 };
        var request = RenderTestFixtures.BuildRequest(
            Source(),
            metadata: RenderTestFixtures.BuildMetadata(),
            limits: limits);

        limits.SourceArtifactLimitBytes = 9999;

        Assert.Equal(1234, request.Limits.SourceArtifactLimitBytes);
        Assert.Same(RenderOutputPolicy.Default, request.OutputPolicy);
    }

    [Fact]
    public void RequestDefaultPolicyIsApplied()
    {
        var request = RenderTestFixtures.BuildRequest(
            Source(),
            metadata: RenderTestFixtures.BuildMetadata(),
            policy: null);

        Assert.Same(RenderOutputPolicy.Default, request.OutputPolicy);
    }

    [Fact]
    public void RequestRejectsMissingRequiredArguments()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var match = RenderTestFixtures.BuildMatch(identity);

        Assert.Throws<ArgumentNullException>(
            () => new RenderRequest(null, null!, match, null, BadgeDefinition.V1Default, "CONFIG"));
        Assert.Throws<ArgumentNullException>(
            () => new RenderRequest(null, identity, null!, null, BadgeDefinition.V1Default, "CONFIG"));
        Assert.Throws<ArgumentNullException>(
            () => new RenderRequest(null, identity, match, null, null!, "CONFIG"));
        Assert.Throws<ArgumentException>(
            () => new RenderRequest(null, identity, match, null, BadgeDefinition.V1Default, string.Empty));
        Assert.Throws<ArgumentException>(
            () => new RenderRequest(null, identity, match, null, Array.Empty<BadgeDefinition>(), "CONFIG"));
    }

    private static SourceImageInput Source()
    {
        return new SourceImageInput(
            SampleBytes,
            "image/png",
            600,
            900,
            SourceImageInput.ComputeSha256(SampleBytes));
    }
}
