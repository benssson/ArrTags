using System;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.9 checks for the bounded render result contract. A failure or
/// cancellation must never carry a partial artifact, and each non-rendered
/// outcome must carry exactly one safe reason code.
/// </summary>
public class RenderResultTests
{
    private static readonly byte[] PngHeader = { 137, 80, 78, 71, 13, 10, 26, 10 };

    [Fact]
    public void RenderedResultCarriesTheArtifactAndIdentity()
    {
        var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };

        var result = RenderResult.Rendered(bytes, 600, 900, new string('A', 64), new string('B', 64));

        Assert.Equal(RenderStatus.Rendered, result.Status);
        Assert.True(result.HasArtifact);
        Assert.Equal(RenderResult.PngContentType, result.ContentType);
        Assert.Equal(bytes, result.PngBytes.ToArray());
        Assert.Equal(600, result.Width);
        Assert.Equal(900, result.Height);
        Assert.Equal(new string('A', 64), result.OutputHash);
        Assert.Equal(new string('B', 64), result.OutputFingerprint);
        Assert.Null(result.PassThroughReason);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void RenderedResultRejectsAMissingOrInvalidPng()
    {
        Assert.Throws<ArgumentException>(
            () => RenderResult.Rendered(Array.Empty<byte>(), 1, 1, "A", "B"));
        Assert.Throws<ArgumentException>(
            () => RenderResult.Rendered(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 1, 1, "A", "B"));
    }

    [Fact]
    public void RenderedResultRejectsInvalidIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderResult.Rendered(PngHeader, 0, 1, "A", "B"));
        Assert.Throws<ArgumentException>(
            () => RenderResult.Rendered(PngHeader, 1, 1, string.Empty, "B"));
        Assert.Throws<ArgumentException>(
            () => RenderResult.Rendered(PngHeader, 1, 1, "A", string.Empty));
    }

    [Fact]
    public void PassThroughResultCarriesNoArtifact()
    {
        var result = RenderResult.PassThrough(RenderPassThroughReason.NoMetadata);

        Assert.Equal(RenderStatus.PassThrough, result.Status);
        Assert.False(result.HasArtifact);
        Assert.Null(result.ContentType);
        Assert.True(result.PngBytes.IsEmpty);
        Assert.Equal(0, result.Width);
        Assert.Equal(0, result.Height);
        Assert.Null(result.OutputHash);
        Assert.Null(result.OutputFingerprint);
        Assert.Equal(RenderPassThroughReason.NoMetadata, result.PassThroughReason);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void FailedResultCarriesNoArtifact()
    {
        var result = RenderResult.Failed(RenderFailureReason.DecodeFailed);

        Assert.Equal(RenderStatus.Failed, result.Status);
        Assert.False(result.HasArtifact);
        Assert.True(result.PngBytes.IsEmpty);
        Assert.Equal(RenderFailureReason.DecodeFailed, result.FailureReason);
        Assert.Null(result.PassThroughReason);
    }

    [Fact]
    public void UndefinedReasonsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderResult.PassThrough((RenderPassThroughReason)999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderResult.Failed((RenderFailureReason)999));
    }
}
