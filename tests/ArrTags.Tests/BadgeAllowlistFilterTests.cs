using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// v1.1 Phase 12 task 12.2 allowlist filtering (ADR-017 clauses 2, 3, and 4). The
/// resolved pre-template value is filtered by a case-insensitive ordinal exact
/// match before the definition template is applied, the allowlist is applied to
/// each retained custom value and to the full audio composite, an empty allowlist
/// means no restriction, and the filter never widens an omission.
/// </summary>
public class BadgeAllowlistFilterTests
{
    private static readonly DateTimeOffset ObservedAt =
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void AllowlistRequiresAnExactCaseInsensitiveMatch()
    {
        var metadata = BuildMetadata(qualityLabel: "Bluray-1080p");

        Assert.Equal(
            new[] { "Bluray-1080p" },
            ResolveTexts(metadata, Quality("bluray-1080p")));

        // No substring, prefix, or wildcard matching is applied.
        Assert.Empty(ResolveTexts(metadata, Quality("Bluray")));
        Assert.Empty(ResolveTexts(metadata, Quality("1080p")));
        Assert.Empty(ResolveTexts(metadata, Quality("Bluray-1080p-extra")));
        Assert.Empty(ResolveTexts(metadata, Quality("Bluray*")));
    }

    [Fact]
    public void IsAllowedTrimsTheResolvedValueAndRejectsPartialMatches()
    {
        Assert.True(BadgeSelectorResolver.IsAllowed("  Bluray-1080p  ", new[] { "Bluray-1080p" }));
        Assert.True(BadgeSelectorResolver.IsAllowed("Bluray-1080p", new[] { "bluray-1080p" }));
        Assert.False(BadgeSelectorResolver.IsAllowed("Bluray-1080p", new[] { "Bluray" }));
        Assert.False(BadgeSelectorResolver.IsAllowed("Bluray-1080p", new[] { "1080p" }));

        // An empty allowlist means no restriction.
        Assert.True(BadgeSelectorResolver.IsAllowed("anything", Array.Empty<string>()));

        Assert.Throws<ArgumentNullException>(() => BadgeSelectorResolver.IsAllowed(null!, new[] { "SDR" }));
        Assert.Throws<ArgumentNullException>(() => BadgeSelectorResolver.IsAllowed("SDR", null!));
    }

    [Fact]
    public void CustomBadgeAllowlistAppliesPerRetainedValue()
    {
        var metadata = BuildMetadata(customBadges: new[] { "HDR", "Dolby", "IMAX" });

        var selection = BadgeDefinitionResolver.Resolve(metadata, new[]
        {
            new BadgeDefinition(
                BadgeSelector.CustomBadge,
                true,
                BadgeDefinition.ValuePlaceholder,
                new[] { "HDR", "IMAX" }),
        });

        Assert.Equal(new[] { "HDR", "IMAX" }, selection.TechnicalValues.Select(value => value.Text));
    }

    [Fact]
    public void AudioAllowlistMatchesTheFullComposite()
    {
        var metadata = BuildMetadata(
            audioCodec: "TrueHD",
            audioChannels: 8,
            audioFeatures: new[] { ArrAudioFeature.Atmos });

        Assert.Equal(
            new[] { "Atmos/TrueHD/8" },
            ResolveTexts(metadata, Audio("Atmos/TrueHD/8")));

        // A component alone is not a match: the allowlist matches the full composite.
        Assert.Empty(ResolveTexts(metadata, Audio("TrueHD")));
        Assert.Empty(ResolveTexts(metadata, Audio("Atmos")));
    }

    [Fact]
    public void UpgradePendingAllowlistPermitsTheFixedStatusText()
    {
        var metadata = BuildMetadata(upgradePending: true);

        var permitted = BadgeDefinitionResolver.Resolve(metadata, new[]
        {
            new BadgeDefinition(
                BadgeSelector.UpgradePending,
                true,
                BadgeSelectorResolver.UpgradeStatusText,
                new[] { BadgeSelectorResolver.UpgradeStatusText }),
        });

        Assert.NotNull(permitted.StatusValue);
        Assert.Equal(BadgeSelectorResolver.UpgradeStatusText, permitted.StatusValue!.Text);

        var suppressed = BadgeDefinitionResolver.Resolve(metadata, new[]
        {
            new BadgeDefinition(
                BadgeSelector.UpgradePending,
                true,
                BadgeSelectorResolver.UpgradeStatusText,
                new[] { "OTHER" }),
        });

        Assert.Null(suppressed.StatusValue);
        Assert.True(suppressed.IsEmpty);
    }

    [Fact]
    public void EmptyAllowlistMeansNoRestriction()
    {
        var metadata = BuildMetadata(
            qualityLabel: "Bluray-1080p",
            upgradePending: true,
            customBadges: new[] { "HDR" });

        var unrestricted = BadgeDefinitionResolver.Resolve(metadata, new[]
        {
            new BadgeDefinition(BadgeSelector.Quality, true, BadgeDefinition.ValuePlaceholder, Array.Empty<string>()),
            new BadgeDefinition(BadgeSelector.CustomBadge, true, BadgeDefinition.ValuePlaceholder, Array.Empty<string>()),
            new BadgeDefinition(BadgeSelector.UpgradePending, true, BadgeSelectorResolver.UpgradeStatusText, null),
        });

        Assert.Equal(new[] { "Bluray-1080p", "HDR" }, unrestricted.TechnicalValues.Select(value => value.Text));
        Assert.NotNull(unrestricted.StatusValue);
    }

    [Fact]
    public void UnknownAndAbsentValuesRemainOmittedWithAnAllowlist()
    {
        // No confirmed quality and no confirmed audio: an allowlist must not infer
        // a value, and the omission is unchanged.
        var metadata = BuildMetadata();

        var selection = BadgeDefinitionResolver.Resolve(metadata, new[]
        {
            new BadgeDefinition(
                BadgeSelector.Quality,
                true,
                BadgeDefinition.ValuePlaceholder,
                new[] { "Bluray-1080p" }),
            new BadgeDefinition(
                BadgeSelector.Audio,
                true,
                BadgeDefinition.ValuePlaceholder,
                new[] { "Atmos/TrueHD/8" }),
        });

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void FilterIsAppliedBeforeTheDefinitionTemplate()
    {
        var metadata = BuildMetadata(qualityLabel: "Bluray-1080p");

        // The allowlist matches the pre-template value, so the template is applied
        // to the retained value.
        var templated = BadgeDefinitionResolver.Resolve(metadata, new[]
        {
            new BadgeDefinition(BadgeSelector.Quality, true, "Q:{value}", new[] { "Bluray-1080p" }),
        });

        Assert.Equal("Q:Bluray-1080p", Assert.Single(templated.TechnicalValues).Text);

        // The templated text is not the comparison value, so an allowlist naming it
        // filters the value out.
        var filtered = BadgeDefinitionResolver.Resolve(metadata, new[]
        {
            new BadgeDefinition(BadgeSelector.Quality, true, "Q:{value}", new[] { "Q:Bluray-1080p" }),
        });

        Assert.True(filtered.IsEmpty);
    }

    private static IReadOnlyList<string> ResolveTexts(BadgeMetadata metadata, BadgeDefinition definition)
    {
        return BadgeDefinitionResolver.Resolve(metadata, new[] { definition })
            .TechnicalValues
            .Select(value => value.Text)
            .ToArray();
    }

    private static BadgeDefinition Quality(params string[] allowedValues)
    {
        return new BadgeDefinition(BadgeSelector.Quality, true, BadgeDefinition.ValuePlaceholder, allowedValues);
    }

    private static BadgeDefinition Audio(params string[] allowedValues)
    {
        return new BadgeDefinition(BadgeSelector.Audio, true, BadgeDefinition.ValuePlaceholder, allowedValues);
    }

    private static BadgeMetadata BuildMetadata(
        string? qualityLabel = null,
        string? audioCodec = null,
        double? audioChannels = null,
        IEnumerable<ArrAudioFeature>? audioFeatures = null,
        bool? upgradePending = null,
        IEnumerable<string>? customBadges = null)
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
        var recordIdentity = new RadarrIdentity(connectionId, 7, ArrFileIdentity.Present(42));

        return new BadgeMetadata(
            provider,
            recordIdentity,
            ObservedAt,
            quality: qualityLabel is null ? null : new ArrQualityDescriptor(qualityLabel, "bluray", 1080, "none", 7),
            audioCodec: audioCodec,
            audioChannels: audioChannels,
            audioFeatures: audioFeatures,
            upgradePending: upgradePending,
            customBadges: customBadges);
    }
}
