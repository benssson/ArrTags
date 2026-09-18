using System;
using System.IO;
using System.Security.Cryptography;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.7 checks for the pinned renderer assets. The bundled DejaVu
/// Sans Bold 2.37 font must be an embedded plugin resource under its stable
/// logical name whose exact bytes match the recorded length and SHA-256, and the
/// default output policy must consume that identity so a changed font asset
/// changes the render fingerprint.
/// </summary>
public class BundledFontTests
{
    [Fact]
    public void BundledIdentityRecordsAdr010FontMetadata()
    {
        var identity = RenderFontIdentity.BundledDejaVuSansBold;

        Assert.Equal("DejaVu Sans", identity.Family);
        Assert.Equal("Bold", identity.Style);
        Assert.Equal("2.37", identity.Version);
        Assert.Equal(RenderFontIdentity.DejaVuSansBoldByteLength, identity.ByteLength);
        Assert.Equal(RenderFontIdentity.DejaVuSansBoldSha256, identity.Sha256);
        Assert.Equal(RenderFontIdentity.DejaVuSansBoldLogicalName, identity.ResourceLogicalName);
    }

    [Fact]
    public void EmbeddedFontResourceExistsUnderStableLogicalName()
    {
        var names = typeof(RenderFontIdentity).Assembly.GetManifestResourceNames();

        Assert.Contains(RenderFontIdentity.DejaVuSansBoldLogicalName, names);

        using var stream = RenderFontIdentity.BundledDejaVuSansBold.OpenResourceStream();

        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
        Assert.Equal(RenderFontIdentity.DejaVuSansBoldByteLength, stream.Length);
    }

    [Fact]
    public void EmbeddedFontBytesMatchRecordedLengthAndSha256()
    {
        using var stream = RenderFontIdentity.BundledDejaVuSansBold.OpenResourceStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        Assert.NotEmpty(bytes);
        Assert.Equal(RenderFontIdentity.DejaVuSansBoldByteLength, bytes.Length);
        Assert.Equal(RenderFontIdentity.DejaVuSansBoldSha256, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void OpenResourceStreamReturnsIndependentReadOnlyStreams()
    {
        using var first = RenderFontIdentity.BundledDejaVuSansBold.OpenResourceStream();
        using var second = RenderFontIdentity.BundledDejaVuSansBold.OpenResourceStream();

        Assert.NotSame(first, second);
        Assert.Equal(first.Length, second.Length);
        Assert.True(second.CanRead);
        Assert.False(second.CanWrite);
    }

    [Fact]
    public void DefaultOutputPolicyConsumesBundledFontIdentity()
    {
        var identity = RenderOutputPolicy.Default.FontIdentity;

        Assert.Same(RenderFontIdentity.BundledDejaVuSansBold, identity);
        Assert.Equal(RenderFontIdentity.DejaVuSansBoldSha256, identity.Sha256);
        Assert.Contains(RenderFontIdentity.DejaVuSansBoldSha256, identity.Descriptor, StringComparison.Ordinal);
        Assert.Contains(RenderFontIdentity.DejaVuSansBoldLogicalName, identity.Descriptor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Other Sans", "Bold", "2.37", 708920, "5C1247ACEF7F2B8522A31742C76D6ADCB5569BACC0BE7CEAA4DC39DD252CE895", "ArrTags.Resources.DejaVuSans-Bold.ttf")]
    [InlineData("DejaVu Sans", "Regular", "2.37", 708920, "5C1247ACEF7F2B8522A31742C76D6ADCB5569BACC0BE7CEAA4DC39DD252CE895", "ArrTags.Resources.DejaVuSans-Bold.ttf")]
    [InlineData("DejaVu Sans", "Bold", "2.36", 708920, "5C1247ACEF7F2B8522A31742C76D6ADCB5569BACC0BE7CEAA4DC39DD252CE895", "ArrTags.Resources.DejaVuSans-Bold.ttf")]
    [InlineData("DejaVu Sans", "Bold", "2.37", 708921, "5C1247ACEF7F2B8522A31742C76D6ADCB5569BACC0BE7CEAA4DC39DD252CE895", "ArrTags.Resources.DejaVuSans-Bold.ttf")]
    [InlineData("DejaVu Sans", "Bold", "2.37", 708920, "0000000000000000000000000000000000000000000000000000000000000000", "ArrTags.Resources.DejaVuSans-Bold.ttf")]
    [InlineData("DejaVu Sans", "Bold", "2.37", 708920, "5C1247ACEF7F2B8522A31742C76D6ADCB5569BACC0BE7CEAA4DC39DD252CE895", "Other.Resource.ttf")]
    public void AnyChangedFontIdentityFieldChangesTheDescriptor(
        string family,
        string style,
        string version,
        int byteLength,
        string sha256,
        string logicalName)
    {
        var changed = new RenderFontIdentity(family, style, version, byteLength, sha256, logicalName);

        Assert.NotEqual(RenderFontIdentity.BundledDejaVuSansBold.Descriptor, changed.Descriptor);
    }

    [Fact]
    public void MissingOrInvalidIdentityFieldsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new RenderFontIdentity(string.Empty, "Bold", "2.37", 1, new string('A', 64), "resource"));
        Assert.Throws<ArgumentException>(() => new RenderFontIdentity("DejaVu Sans", "Bold", "2.37", 1, "AB", "resource"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderFontIdentity("DejaVu Sans", "Bold", "2.37", 0, new string('A', 64), "resource"));
    }
}
