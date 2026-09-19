using System;
using ArrTags.Artwork;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.3 checks that the adapter's header descriptor reports the same
/// encoded dimensions and EXIF orientation that the renderer decodes. The valid
/// decode cases need the pinned native raster runtime and are guarded; the
/// malformed cases fail closed with or without the native runtime.
/// </summary>
public sealed class SourceImageDescriptorTests
{
    /// <summary>
    /// Gets the EXIF orientation to provider-neutral orientation mapping for all
    /// eight values.
    /// </summary>
    public static TheoryData<ushort, SourceOrientation> Orientations =>
        new()
        {
            { 1, SourceOrientation.TopLeft },
            { 2, SourceOrientation.TopRight },
            { 3, SourceOrientation.BottomRight },
            { 4, SourceOrientation.BottomLeft },
            { 5, SourceOrientation.LeftTop },
            { 6, SourceOrientation.RightTop },
            { 7, SourceOrientation.RightBottom },
            { 8, SourceOrientation.LeftBottom },
        };

    [SkiaNativeFact]
    public void PngHeaderReportsEncodedDimensionsAndNoOrientation()
    {
        var bytes = RenderImageFixtures.CreateRgbPng(20, 30).Bytes.ToArray();

        Assert.True(SourceImageDescriptor.TryRead(bytes, out var width, out var height, out var orientation));

        Assert.Equal(20, width);
        Assert.Equal(30, height);
        Assert.Equal(SourceOrientation.TopLeft, orientation);
    }

    [SkiaNativeTheory]
    [MemberData(nameof(Orientations))]
    public void JpegHeaderReportsEncodedDimensionsAndOrientation(ushort exif, SourceOrientation expected)
    {
        var bytes = RenderImageFixtures.CreateTwoMarkerOrientedJpeg(40, 60, exif);

        Assert.True(SourceImageDescriptor.TryRead(bytes, out var width, out var height, out var orientation));

        Assert.Equal(40, width);
        Assert.Equal(60, height);
        Assert.Equal(expected, orientation);
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0x03, 0x04 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [InlineData(new byte[] { })]
    public void MalformedOrUnsupportedHeadersFailClosed(byte[] bytes)
    {
        Assert.False(SourceImageDescriptor.TryRead(bytes, out var width, out var height, out var orientation));
        Assert.Equal(0, width);
        Assert.Equal(0, height);
        Assert.Equal(SourceOrientation.TopLeft, orientation);
    }
}
