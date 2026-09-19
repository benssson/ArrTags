using System;
using ArrTags.Artwork;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.3 checks for the V1 source-container confinement. The renderer
/// inspects color profiles only for PNG <c>iCCP</c> and JPEG <c>APP2</c>, so the
/// adapter must recognize only those containers and reject every other one.
/// </summary>
public sealed class ArtworkSourceContentTypeTests
{
    [Theory]
    [InlineData("PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("JPEG", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    public void ConfinedContainersAreDetected(string name, byte[] bytes)
    {
        Assert.NotEmpty(name);
        Assert.True(ArtworkSourceContentType.TryDetect(bytes, out var contentType));
        Assert.True(ArtworkSourceContentType.IsConfined(contentType));
    }

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 })]
    [InlineData(new byte[] { 0x42, 0x4D })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(new byte[] { 0xFF, 0xD8 })]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { })]
    public void UninspectedOrTruncatedContainersAreRejected(byte[] bytes)
    {
        Assert.False(ArtworkSourceContentType.TryDetect(bytes, out var contentType));
        Assert.Equal(string.Empty, contentType);
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("IMAGE/PNG", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/webp", false)]
    [InlineData("image/gif", false)]
    [InlineData("image/avif", false)]
    [InlineData("image/jpg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ConfinementIsLimitedToPngAndJpeg(string? contentType, bool expected)
    {
        Assert.Equal(expected, ArtworkSourceContentType.IsConfined(contentType));
    }

    [Fact]
    public void ContentTypesMatchTheRendererContract()
    {
        Assert.Equal("image/png", ArtworkSourceContentType.Png);
        Assert.Equal("image/jpeg", ArtworkSourceContentType.Jpeg);
    }
}
