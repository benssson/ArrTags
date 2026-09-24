using System;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.4 checks for the render request and result fingerprints. Every
/// output-affecting value, including the renderer and badge schema versions, must
/// participate; correlation identifiers and timestamps must not.
/// </summary>
public class RenderFingerprintTests
{
    private static readonly Guid DefaultItemId = new Guid("11111111-1111-1111-1111-111111111111");

    private static readonly Guid OtherItemId = new Guid("22222222-2222-2222-2222-222222222222");

    private static BadgeSelection DefaultSelection { get; } = new BadgeSelection(
        new[]
        {
            new BadgeValue(BadgeSelector.Quality, "Bluray-1080p"),
            new BadgeValue(BadgeSelector.Resolution, "1080p"),
        },
        new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE"));

    [Fact]
    public void RequestAndOutputFingerprintsAreDeterministicHex()
    {
        var input = BuildInput();

        var output = RenderFingerprint.ComputeOutputFingerprint(input);
        var request = RenderFingerprint.ComputeRequestFingerprint(input);

        Assert.Matches("^[0-9A-F]{64}$", output);
        Assert.Matches("^[0-9A-F]{64}$", request);
        Assert.Equal(output, RenderFingerprint.ComputeOutputFingerprint(BuildInput()));
        Assert.Equal(request, RenderFingerprint.ComputeRequestFingerprint(BuildInput()));
    }

    [Fact]
    public void OutputFingerprintIsIndependentOfItemIdentityButRequestFingerprintIsNot()
    {
        var first = BuildInput(itemId: DefaultItemId, itemType: MediaItemType.Movie);
        var second = BuildInput(itemId: OtherItemId, itemType: MediaItemType.Movie);
        var episode = BuildInput(itemId: DefaultItemId, itemType: MediaItemType.Episode);

        Assert.Equal(
            RenderFingerprint.ComputeOutputFingerprint(first),
            RenderFingerprint.ComputeOutputFingerprint(second));
        Assert.Equal(
            RenderFingerprint.ComputeOutputFingerprint(first),
            RenderFingerprint.ComputeOutputFingerprint(episode));

        Assert.NotEqual(
            RenderFingerprint.ComputeRequestFingerprint(first),
            RenderFingerprint.ComputeRequestFingerprint(second));
        Assert.NotEqual(
            RenderFingerprint.ComputeRequestFingerprint(first),
            RenderFingerprint.ComputeRequestFingerprint(episode));
    }

    [Fact]
    public void SourceFingerprintChangesBothFingerprints()
    {
        AssertOutputDiffers(BuildInput(sourceFingerprint: "SOURCE-B"));
        AssertRequestDiffers(BuildInput(sourceFingerprint: "SOURCE-B"));
    }

    [Fact]
    public void SourceDimensionsChangeTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(width: 1200));
        AssertOutputDiffers(BuildInput(height: 1800));
    }

    [Fact]
    public void MetadataPresenceAndValueChangeTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(metadataFingerprint: "META-B"));
        AssertOutputDiffers(BuildInput(metadataFingerprint: null));
    }

    [Fact]
    public void ConfigurationFingerprintChangesTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(configurationFingerprint: "CONFIG-B"));
    }

    [Fact]
    public void SelectedValuesAndOrderChangeTheOutputFingerprint()
    {
        var reordered = new BadgeSelection(
            new[]
            {
                new BadgeValue(BadgeSelector.Resolution, "1080p"),
                new BadgeValue(BadgeSelector.Quality, "Bluray-1080p"),
            },
            new BadgeValue(BadgeSelector.UpgradePending, "UPGRADE"));
        var withoutStatus = new BadgeSelection(
            new[]
            {
                new BadgeValue(BadgeSelector.Quality, "Bluray-1080p"),
                new BadgeValue(BadgeSelector.Resolution, "1080p"),
            },
            null);

        AssertOutputDiffers(BuildInput(selection: reordered));
        AssertOutputDiffers(BuildInput(selection: withoutStatus));
        AssertOutputDiffers(BuildInput(selection: BadgeSelection.Empty));
    }

    [Fact]
    public void PaletteChangesTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { TechnicalBackground = "#000000" }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { StatusText = "#000000" }));
    }

    [Fact]
    public void FontIdentityChangesTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy
        {
            FontIdentity = new RenderFontIdentity("Other Font", "Bold", "1.0", 1024, new string('A', 64), "Other.Font.ttf"),
        }));
    }

    [Fact]
    public void BundledFontSha256ChangesTheOutputFingerprint()
    {
        var changed = new RenderFontIdentity(
            "DejaVu Sans",
            "Bold",
            "2.37",
            RenderFontIdentity.DejaVuSansBoldByteLength,
            new string('A', 64),
            RenderFontIdentity.DejaVuSansBoldLogicalName);

        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { FontIdentity = changed }));
    }

    [Fact]
    public void OutputFormatAndColorPolicyChangeTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { OutputFormat = "image/webp" }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { ColorSpace = "Display P3" }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { AlphaPolicy = "Flatten" }));
    }

    [Fact]
    public void ScalePolicyChangesTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { ScaleReferenceWidth = 1200 }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { MinimumScale = 0.25 }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { MaximumScale = 2.0 }));
    }

    [Fact]
    public void TextLimitsChangeTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { MaximumScalarValues = 32 }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { RetainedPrefixScalarValues = 29 }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { Ellipsis = ".." }));
    }

    [Fact]
    public void BadgePositionAndSizeChangeTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { Position = BadgePosition.TopRight }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { Position = BadgePosition.Center }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { Size = BadgeSize.Small }));
        AssertOutputDiffers(BuildInput(policy: new RenderOutputPolicy { Size = BadgeSize.Large }));

        // The V1 default is identity-neutral, so an explicit default policy has
        // the same output fingerprint as the code-owned default.
        Assert.Equal(
            RenderFingerprint.ComputeOutputFingerprint(BuildInput()),
            RenderFingerprint.ComputeOutputFingerprint(BuildInput(policy: new RenderOutputPolicy
            {
                Position = BadgePosition.BottomLeft,
                Size = BadgeSize.Medium,
            })));
    }

    [Fact]
    public void RendererVersionChangesTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(rendererVersion: RenderVersion.CurrentRendererVersion + 1));
    }

    [Fact]
    public void BadgeSchemaVersionChangesTheOutputFingerprint()
    {
        AssertOutputDiffers(BuildInput(badgeSchemaVersion: RenderVersion.CurrentBadgeSchemaVersion + 1));
    }

    [Fact]
    public void ObservationTimestampDoesNotChangeTheFingerprint()
    {
        var firstMetadata = BuildMetadata(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var secondMetadata = BuildMetadata(new DateTimeOffset(2030, 6, 7, 8, 9, 10, TimeSpan.Zero));

        var first = BuildInput(
            metadataFingerprint: firstMetadata.MetadataFingerprint,
            selection: BadgeSelectorResolver.Resolve(firstMetadata));
        var second = BuildInput(
            metadataFingerprint: secondMetadata.MetadataFingerprint,
            selection: BadgeSelectorResolver.Resolve(secondMetadata));

        Assert.Equal(
            RenderFingerprint.ComputeOutputFingerprint(first),
            RenderFingerprint.ComputeOutputFingerprint(second));
        Assert.Equal(
            RenderFingerprint.ComputeRequestFingerprint(first),
            RenderFingerprint.ComputeRequestFingerprint(second));
    }

    [Fact]
    public void EmptyItemIdentifierIsRejected()
    {
        Assert.Throws<ArgumentException>(() => BuildInput(itemId: Guid.Empty));
    }

    [Fact]
    public void UndefinedItemTypeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildInput(itemType: (MediaItemType)99));
    }

    [Fact]
    public void EmptyRequiredStringsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => BuildInput(sourceFingerprint: string.Empty));
        Assert.Throws<ArgumentException>(() => BuildInput(configurationFingerprint: string.Empty));
        Assert.Throws<ArgumentException>(() => BuildInput(metadataFingerprint: string.Empty));
    }

    [Fact]
    public void NonPositiveDimensionsAndVersionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildInput(width: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildInput(height: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildInput(rendererVersion: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildInput(badgeSchemaVersion: -1));
    }

    [Fact]
    public void NullSelectionAndPolicyAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new RenderFingerprintInput(
            DefaultItemId,
            MediaItemType.Movie,
            "SOURCE-A",
            1000,
            1500,
            "META-A",
            "CONFIG-A",
            null!,
            RenderOutputPolicy.Default));

        Assert.Throws<ArgumentNullException>(() => new RenderFingerprintInput(
            DefaultItemId,
            MediaItemType.Movie,
            "SOURCE-A",
            1000,
            1500,
            "META-A",
            "CONFIG-A",
            DefaultSelection,
            null!));
    }

    private static void AssertOutputDiffers(RenderFingerprintInput changed)
    {
        Assert.NotEqual(
            RenderFingerprint.ComputeOutputFingerprint(BuildInput()),
            RenderFingerprint.ComputeOutputFingerprint(changed));
    }

    private static void AssertRequestDiffers(RenderFingerprintInput changed)
    {
        Assert.NotEqual(
            RenderFingerprint.ComputeRequestFingerprint(BuildInput()),
            RenderFingerprint.ComputeRequestFingerprint(changed));
    }

    private static RenderFingerprintInput BuildInput(
        Guid? itemId = null,
        MediaItemType itemType = MediaItemType.Movie,
        string sourceFingerprint = "SOURCE-A",
        int width = 1000,
        int height = 1500,
        string? metadataFingerprint = "META-A",
        string configurationFingerprint = "CONFIG-A",
        BadgeSelection? selection = null,
        RenderOutputPolicy? policy = null,
        int rendererVersion = RenderVersion.CurrentRendererVersion,
        int badgeSchemaVersion = RenderVersion.CurrentBadgeSchemaVersion)
    {
        return new RenderFingerprintInput(
            itemId ?? DefaultItemId,
            itemType,
            sourceFingerprint,
            width,
            height,
            metadataFingerprint,
            configurationFingerprint,
            selection ?? DefaultSelection,
            policy ?? RenderOutputPolicy.Default,
            rendererVersion,
            badgeSchemaVersion);
    }

    private static BadgeMetadata BuildMetadata(DateTimeOffset observedAt)
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var identity = new RadarrIdentity(
            ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878"),
            7,
            ArrFileIdentity.Present(42));

        return new BadgeMetadata(
            provider,
            identity,
            observedAt,
            quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "none", 7),
            resolution: new ArrResolutionDescriptor(1920, 1080, "1080p", ArrMetadataOrigin.ProviderMediaInfo),
            videoCodec: "x265");
    }
}
