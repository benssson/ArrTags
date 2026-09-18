using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ArrTags.Metadata;

namespace ArrTags.Rendering;

/// <summary>
/// Resolves the provider-neutral ADR-009 selector vocabulary against canonical
/// <see cref="BadgeMetadata"/>. The resolver reads only the canonical model: it
/// never consults a provider DTO, record identifier, credential, or extension
/// value, and it never turns an unknown or absent field into a claim. A field is
/// emitted only from a confirmed value.
/// </summary>
public static class BadgeSelectorResolver
{
    /// <summary>
    /// The fixed upgrade-status text.
    /// </summary>
    public const string UpgradeStatusText = "UPGRADE";

    /// <summary>
    /// The confirmed Dolby Vision label, which replaces the generic
    /// dynamic-range label.
    /// </summary>
    public const string DolbyVisionLabel = "DV";

    private static readonly IReadOnlyList<BadgeSelector> TechnicalPriority =
        new[]
        {
            BadgeSelector.Quality,
            BadgeSelector.Resolution,
            BadgeSelector.DynamicRange,
            BadgeSelector.Source,
            BadgeSelector.VideoCodec,
            BadgeSelector.Audio,
            BadgeSelector.CustomBadge,
        };

    private static readonly IReadOnlyList<ArrAudioFeature> AudioFeaturePriority =
        new[]
        {
            ArrAudioFeature.Atmos,
            ArrAudioFeature.DtsX,
            ArrAudioFeature.DtsHd,
            ArrAudioFeature.Dts,
        };

    /// <summary>
    /// Gets every V1 selector enabled by default. Definition order may disable a
    /// selector, but it cannot change the semantic priority applied here.
    /// </summary>
    public static IReadOnlySet<BadgeSelector> DefaultSelectors { get; } =
        Enum.GetValues<BadgeSelector>().ToFrozenSet();

    /// <summary>
    /// Resolves the enabled selectors against one canonical metadata observation.
    /// </summary>
    /// <param name="metadata">The canonical metadata observation, or <see langword="null"/> when none exists.</param>
    /// <param name="enabledSelectors">The enabled selectors, or <see langword="null"/> for <see cref="DefaultSelectors"/>.</param>
    /// <returns>The ordered selection, or <see cref="BadgeSelection.Empty"/> when no value is confirmed.</returns>
    public static BadgeSelection Resolve(
        BadgeMetadata? metadata,
        IReadOnlySet<BadgeSelector>? enabledSelectors = null)
    {
        if (metadata is null)
        {
            return BadgeSelection.Empty;
        }

        var enabled = enabledSelectors ?? DefaultSelectors;
        var technicalValues = new List<BadgeValue>();

        foreach (var selector in TechnicalPriority)
        {
            if (!enabled.Contains(selector))
            {
                continue;
            }

            AddSelector(metadata, selector, technicalValues);
        }

        var status = enabled.Contains(BadgeSelector.UpgradePending)
            && metadata.UpgradePending == true
            ? new BadgeValue(BadgeSelector.UpgradePending, UpgradeStatusText)
            : null;

        return technicalValues.Count == 0 && status is null
            ? BadgeSelection.Empty
            : new BadgeSelection(technicalValues, status);
    }

    private static void AddSelector(BadgeMetadata metadata, BadgeSelector selector, List<BadgeValue> values)
    {
        switch (selector)
        {
            case BadgeSelector.Quality:
                Add(values, selector, Normalize(metadata.Quality?.Label));
                break;
            case BadgeSelector.Resolution:
                Add(values, selector, Normalize(metadata.Resolution?.Label));
                break;
            case BadgeSelector.DynamicRange:
                Add(values, selector, ResolveDynamicRange(metadata));
                break;
            case BadgeSelector.Source:
                Add(values, selector, Normalize(metadata.Source));
                break;
            case BadgeSelector.VideoCodec:
                Add(values, selector, Normalize(metadata.VideoCodec));
                break;
            case BadgeSelector.Audio:
                Add(values, selector, ResolveAudio(metadata));
                break;
            case BadgeSelector.CustomBadge:
                foreach (var customBadge in metadata.CustomBadges)
                {
                    Add(values, selector, Normalize(customBadge));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown badge selector.");
        }
    }

    private static void Add(List<BadgeValue> values, BadgeSelector selector, string? text)
    {
        if (text is not null)
        {
            values.Add(new BadgeValue(selector, text));
        }
    }

    private static string? ResolveDynamicRange(BadgeMetadata metadata)
    {
        if (metadata.DolbyVision == true)
        {
            return DolbyVisionLabel;
        }

        return metadata.DynamicRange?.Kind switch
        {
            ArrDynamicRangeKind.Sdr => "SDR",
            ArrDynamicRangeKind.Hdr => "HDR",
            ArrDynamicRangeKind.Hdr10 => "HDR10",
            ArrDynamicRangeKind.Hdr10Plus => "HDR10+",
            ArrDynamicRangeKind.Hlg => "HLG",
            ArrDynamicRangeKind.DolbyVision => DolbyVisionLabel,
            _ => null,
        };
    }

    private static string? ResolveAudio(BadgeMetadata metadata)
    {
        var tokens = new List<string>();

        var features = metadata.AudioFeatures;
        if (features is not null)
        {
            foreach (var feature in AudioFeaturePriority)
            {
                if (features.Contains(feature))
                {
                    tokens.Add(FeatureLabel(feature));
                }
            }
        }

        var codec = Normalize(metadata.AudioCodec);
        if (codec is not null)
        {
            tokens.Add(codec);
        }

        if (metadata.AudioChannels is double channels
            && !double.IsNaN(channels)
            && !double.IsInfinity(channels)
            && channels > 0)
        {
            tokens.Add(channels.ToString("0.###", CultureInfo.InvariantCulture));
        }

        return tokens.Count == 0 ? null : string.Join("/", tokens);
    }

    private static string FeatureLabel(ArrAudioFeature feature)
    {
        return feature switch
        {
            ArrAudioFeature.Atmos => "Atmos",
            ArrAudioFeature.DtsX => "DTS-X",
            ArrAudioFeature.DtsHd => "DTS-HD",
            ArrAudioFeature.Dts => "DTS",
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown audio feature."),
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
