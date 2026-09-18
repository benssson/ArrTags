using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.3 checks that unknown technical values remain distinct from
/// confirmed negative values. Under ADR-009 neither state is displayable, but the
/// resolver must never infer a negative from missing data, never render a
/// confirmed negative as a claim, and never turn an unknown value into a
/// confirmed one. The canonical metadata fingerprint retains the distinction.
/// </summary>
public class BadgeUnknownValueTests
{
    private static readonly DateTimeOffset ObservedAt =
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void UnknownTechnicalValuesAreNotRenderedAsPlaceholders()
    {
        var metadata = BuildMetadata();

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.True(selection.IsEmpty);
        Assert.Empty(selection.TechnicalValues);
        Assert.Null(selection.StatusValue);
    }

    [Fact]
    public void ConfirmedNegativeDolbyVisionDoesNotProduceADvClaim()
    {
        var metadata = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Hdr10,
                "HDR10",
                ArrMetadataOrigin.ProviderMediaInfo),
            dolbyVision: false);

        var selection = BadgeSelectorResolver.Resolve(metadata);

        var value = Assert.Single(selection.TechnicalValues);
        Assert.Equal(BadgeSelector.DynamicRange, value.Selector);
        Assert.Equal("HDR10", value.Text);
    }

    [Fact]
    public void ConfirmedNegativeDolbyVisionWithoutRangeIsOmitted()
    {
        var metadata = BuildMetadata(dolbyVision: false);

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void UnknownAndConfirmedNegativeDolbyVisionAreDistinctButBothNonDisplayable()
    {
        var unknown = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Hdr10,
                "HDR10",
                ArrMetadataOrigin.ProviderMediaInfo),
            dolbyVision: null);
        var negative = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Hdr10,
                "HDR10",
                ArrMetadataOrigin.ProviderMediaInfo),
            dolbyVision: false);

        Assert.NotEqual(unknown.MetadataFingerprint, negative.MetadataFingerprint);

        Assert.Equal(
            new[] { "HDR10" },
            BadgeSelectorResolver.Resolve(unknown).TechnicalValues.Select(value => value.Text));
        Assert.Equal(
            new[] { "HDR10" },
            BadgeSelectorResolver.Resolve(negative).TechnicalValues.Select(value => value.Text));
    }

    [Fact]
    public void ConfirmedDolbyVisionIsTheOnlyDvSource()
    {
        var metadata = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Hdr10,
                "HDR10",
                ArrMetadataOrigin.ProviderMediaInfo),
            dolbyVision: true);

        var value = Assert.Single(BadgeSelectorResolver.Resolve(metadata).TechnicalValues);
        Assert.Equal("DV", value.Text);
    }

    [Fact]
    public void ConfirmedSdrRangeIsDisplayedWhileUnknownRangeIsOmitted()
    {
        var sdr = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Sdr,
                "SDR",
                ArrMetadataOrigin.ProviderMediaInfo));
        var unknown = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Unknown,
                "unrecognized",
                ArrMetadataOrigin.ProviderMediaInfo));

        Assert.Equal(
            new[] { "SDR" },
            BadgeSelectorResolver.Resolve(sdr).TechnicalValues.Select(value => value.Text));
        Assert.True(BadgeSelectorResolver.Resolve(unknown).IsEmpty);
        Assert.NotEqual(sdr.MetadataFingerprint, unknown.MetadataFingerprint);
    }

    [Fact]
    public void UnknownAndConfirmedNegativeUpgradePendingAreDistinctButBothNonDisplayable()
    {
        var unknown = BuildMetadata(upgradePending: null);
        var negative = BuildMetadata(upgradePending: false);

        Assert.NotEqual(unknown.MetadataFingerprint, negative.MetadataFingerprint);

        Assert.Null(BadgeSelectorResolver.Resolve(unknown).StatusValue);
        Assert.Null(BadgeSelectorResolver.Resolve(negative).StatusValue);
    }

    [Fact]
    public void ConfirmedNegativeUpgradePendingIsNotRenderedAsFalse()
    {
        var metadata = BuildMetadata(upgradePending: false);

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.True(selection.IsEmpty);
        Assert.Null(selection.StatusValue);
    }

    [Fact]
    public void UnknownAudioFeaturesDoNotSuppressAConfirmedCodec()
    {
        var metadata = BuildMetadata(
            audioCodec: "EAC3",
            audioChannels: 5.1,
            audioFeatures: null);

        var value = Assert.Single(BadgeSelectorResolver.Resolve(metadata).TechnicalValues);
        Assert.Equal(BadgeSelector.Audio, value.Selector);
        Assert.Equal("EAC3/5.1", value.Text);
    }

    [Fact]
    public void ConfirmedEmptyAudioFeaturesDoNotInferFeaturesFromCodec()
    {
        var metadata = BuildMetadata(
            audioCodec: "EAC3",
            audioChannels: 5.1,
            audioFeatures: Array.Empty<ArrAudioFeature>());

        var value = Assert.Single(BadgeSelectorResolver.Resolve(metadata).TechnicalValues);
        Assert.Equal("EAC3/5.1", value.Text);
        Assert.DoesNotContain("Atmos", value.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAndEmptyAudioFeaturesAreDistinctInTheFingerprint()
    {
        var unknown = BuildMetadata(audioCodec: "EAC3", audioFeatures: null);
        var empty = BuildMetadata(audioCodec: "EAC3", audioFeatures: Array.Empty<ArrAudioFeature>());

        Assert.NotEqual(unknown.MetadataFingerprint, empty.MetadataFingerprint);
    }

    private static BadgeMetadata BuildMetadata(
        ArrQualityDescriptor? quality = null,
        ArrResolutionDescriptor? resolution = null,
        ArrDynamicRangeDescriptor? dynamicRange = null,
        bool? dolbyVision = null,
        string? videoCodec = null,
        string? audioCodec = null,
        double? audioChannels = null,
        IEnumerable<ArrAudioFeature>? audioFeatures = null,
        string? source = null,
        bool? upgradePending = null,
        IEnumerable<string>? customBadges = null)
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var identity = new RadarrIdentity(
            ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878"),
            7,
            ArrFileIdentity.Present(42));

        return new BadgeMetadata(
            provider,
            identity,
            ObservedAt,
            quality: quality,
            resolution: resolution,
            dynamicRange: dynamicRange,
            dolbyVision: dolbyVision,
            videoCodec: videoCodec,
            audioCodec: audioCodec,
            audioChannels: audioChannels,
            audioFeatures: audioFeatures,
            source: source,
            upgradePending: upgradePending,
            customBadges: customBadges);
    }
}
