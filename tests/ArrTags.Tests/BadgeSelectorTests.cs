using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.1 checks for provider-neutral metadata selectors. The resolver
/// must read only canonical <see cref="BadgeMetadata"/>, emit values in the
/// ADR-009 priority order, and never turn an unknown or absent field into a
/// claim.
/// </summary>
public class BadgeSelectorTests
{
    private static readonly DateTimeOffset ObservedAt =
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void ResolvesDefaultTechnicalValuesInPriorityOrder()
    {
        var metadata = BuildMetadata(
            quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "none", 7),
            resolution: new ArrResolutionDescriptor(1920, 1080, "1080p", ArrMetadataOrigin.ProviderMediaInfo),
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Hdr10,
                "HDR10",
                ArrMetadataOrigin.ProviderMediaInfo),
            videoCodec: "x265",
            audioCodec: "TrueHD",
            audioChannels: 8,
            audioFeatures: new[] { ArrAudioFeature.Atmos },
            source: "bluray",
            upgradePending: true,
            customBadges: new[] { "HDR", "DV" });

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.Equal(
            new[]
            {
                BadgeSelector.Quality,
                BadgeSelector.Resolution,
                BadgeSelector.DynamicRange,
                BadgeSelector.Source,
                BadgeSelector.VideoCodec,
                BadgeSelector.Audio,
                BadgeSelector.CustomBadge,
                BadgeSelector.CustomBadge,
            },
            selection.TechnicalValues.Select(value => value.Selector));

        Assert.Equal(
            new[] { "Bluray-1080p", "1080p", "HDR10", "bluray", "x265", "Atmos/TrueHD/8", "HDR", "DV" },
            selection.TechnicalValues.Select(value => value.Text));

        Assert.All(selection.TechnicalValues, value => Assert.Equal(BadgeValueKind.Technical, value.Kind));

        Assert.NotNull(selection.StatusValue);
        Assert.Equal(BadgeSelector.UpgradePending, selection.StatusValue!.Selector);
        Assert.Equal(BadgeValueKind.Status, selection.StatusValue.Kind);
        Assert.Equal("UPGRADE", selection.StatusValue.Text);
        Assert.False(selection.IsEmpty);
    }

    [Fact]
    public void ConfirmedDolbyVisionReplacesGenericDynamicRange()
    {
        var metadata = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Hdr10,
                "HDR10",
                ArrMetadataOrigin.ProviderMediaInfo),
            dolbyVision: true);

        var selection = BadgeSelectorResolver.Resolve(metadata);

        var value = Assert.Single(selection.TechnicalValues);
        Assert.Equal(BadgeSelector.DynamicRange, value.Selector);
        Assert.Equal("DV", value.Text);
    }

    [Fact]
    public void UnknownDynamicRangeIsOmitted()
    {
        var metadata = BuildMetadata(
            dynamicRange: new ArrDynamicRangeDescriptor(
                ArrDynamicRangeKind.Unknown,
                "something",
                ArrMetadataOrigin.ProviderMediaInfo));

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void UnconfirmedDolbyVisionIsNotInferredFromDynamicRange()
    {
        var metadata = BuildMetadata();

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void AudioFeatureOrderIsFixedAndPrecedesCodecAndChannels()
    {
        var metadata = BuildMetadata(
            audioCodec: "DTS-HD MA",
            audioChannels: 7.1,
            audioFeatures: new[]
            {
                ArrAudioFeature.Dts,
                ArrAudioFeature.DtsHd,
                ArrAudioFeature.DtsX,
                ArrAudioFeature.Atmos,
            });

        var selection = BadgeSelectorResolver.Resolve(metadata);

        var value = Assert.Single(selection.TechnicalValues);
        Assert.Equal(BadgeSelector.Audio, value.Selector);
        Assert.Equal("Atmos/DTS-X/DTS-HD/DTS/DTS-HD MA/7.1", value.Text);
    }

    [Fact]
    public void AudioUsesCodecAndChannelsWhenFeatureStateIsUnknown()
    {
        var metadata = BuildMetadata(
            audioCodec: "EAC3",
            audioChannels: 5.1,
            audioFeatures: null);

        var selection = BadgeSelectorResolver.Resolve(metadata);

        var value = Assert.Single(selection.TechnicalValues);
        Assert.Equal("EAC3/5.1", value.Text);
    }

    [Fact]
    public void AudioWithEmptyFeatureSetStillUsesConfirmedCodec()
    {
        var metadata = BuildMetadata(
            audioCodec: "AAC",
            audioFeatures: Array.Empty<ArrAudioFeature>());

        var selection = BadgeSelectorResolver.Resolve(metadata);

        var value = Assert.Single(selection.TechnicalValues);
        Assert.Equal("AAC", value.Text);
    }

    [Fact]
    public void AudioIsOmittedWhenNoComponentIsConfirmed()
    {
        var metadata = BuildMetadata();

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void QualityIsOmittedWithoutAConfirmedLabel()
    {
        var metadata = BuildMetadata(
            quality: new ArrQualityDescriptor(null, "bluray", 1080, null, 7),
            source: "bluray");

        var selection = BadgeSelectorResolver.Resolve(
            metadata,
            new HashSet<BadgeSelector> { BadgeSelector.Quality });

        Assert.True(selection.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void UpgradeStatusRequiresConfirmedTrue(bool? upgradePending)
    {
        var metadata = BuildMetadata(upgradePending: upgradePending);

        var selection = BadgeSelectorResolver.Resolve(metadata);

        Assert.Null(selection.StatusValue);
        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void DisabledSelectorsAreOmittedButPriorityIsUnchanged()
    {
        var metadata = BuildMetadata(
            quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "none", 7),
            resolution: new ArrResolutionDescriptor(1920, 1080, "1080p", ArrMetadataOrigin.ProviderMediaInfo),
            videoCodec: "x265",
            upgradePending: true);

        var selection = BadgeSelectorResolver.Resolve(
            metadata,
            new HashSet<BadgeSelector> { BadgeSelector.VideoCodec, BadgeSelector.Resolution, BadgeSelector.UpgradePending });

        Assert.Equal(new[] { "1080p", "x265" }, selection.TechnicalValues.Select(value => value.Text));
        Assert.NotNull(selection.StatusValue);
    }

    [Fact]
    public void CustomBadgesProduceOneCandidatePerRetainedValue()
    {
        var metadata = BuildMetadata(customBadges: new[] { "HDR", "Dolby", "IMAX" });

        var selection = BadgeSelectorResolver.Resolve(
            metadata,
            new HashSet<BadgeSelector> { BadgeSelector.CustomBadge });

        Assert.Equal(new[] { "HDR", "Dolby", "IMAX" }, selection.TechnicalValues.Select(value => value.Text));
        Assert.All(
            selection.TechnicalValues,
            value => Assert.Equal(BadgeSelector.CustomBadge, value.Selector));
    }

    [Fact]
    public void NullMetadataResolvesToEmptySelection()
    {
        var selection = BadgeSelectorResolver.Resolve(null);

        Assert.Same(BadgeSelection.Empty, selection);
        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void EmptyEnabledSetResolvesToEmptySelection()
    {
        var metadata = BuildMetadata(
            quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "none", 7),
            upgradePending: true);

        var selection = BadgeSelectorResolver.Resolve(
            metadata,
            new HashSet<BadgeSelector>());

        Assert.True(selection.IsEmpty);
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
