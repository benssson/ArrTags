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
/// emitted only from a confirmed value; a confirmed negative value is distinct
/// from an unknown value, but neither is displayable and neither may be inferred
/// from the other.
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

        var statusText = ResolveConfirmedTrue(metadata.UpgradePending, UpgradeStatusText);
        var status = enabled.Contains(BadgeSelector.UpgradePending) && statusText is not null
            ? new BadgeValue(BadgeSelector.UpgradePending, statusText)
            : null;

        return technicalValues.Count == 0 && status is null
            ? BadgeSelection.Empty
            : new BadgeSelection(technicalValues, status);
    }

    /// <summary>
    /// Determines whether a resolved, pre-template selector value is permitted by
    /// a resolved allowlist (ADR-017). An empty allowlist means no restriction.
    /// Otherwise the value is trimmed and compared by a case-insensitive ordinal
    /// exact match against the allowlist entries; substring, wildcard, prefix,
    /// and regular-expression matching are never applied. The filter can only
    /// remove an already-confirmed value and never widens an omission.
    /// </summary>
    /// <param name="value">The confirmed, pre-template resolved value.</param>
    /// <param name="allowedValues">The resolved allowlist; empty means no restriction.</param>
    /// <returns><see langword="true"/> when the value is permitted.</returns>
    /// <exception cref="ArgumentNullException">The value or the allowlist is <see langword="null"/>.</exception>
    public static bool IsAllowed(string value, IReadOnlyList<string> allowedValues)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(allowedValues);

        if (allowedValues.Count == 0)
        {
            return true;
        }

        var candidate = value.Trim();
        foreach (var allowed in allowedValues)
        {
            if (string.Equals(candidate, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        var dolbyVision = ResolveConfirmedTrue(metadata.DolbyVision, DolbyVisionLabel);
        if (dolbyVision is not null)
        {
            return dolbyVision;
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

    /// <summary>
    /// Resolves a tri-state canonical technical flag to its display text. Only a
    /// confirmed <see langword="true"/> produces a value. A confirmed negative
    /// (<see langword="false"/>) and an unknown value (<see langword="null"/>) are
    /// distinct canonical states; neither is displayable, and the resolver must
    /// never infer one from the other or render either as a claim.
    /// </summary>
    /// <param name="value">The tri-state canonical flag.</param>
    /// <param name="displayText">The display text for a confirmed positive value.</param>
    /// <returns>The display text for a confirmed positive value; otherwise <see langword="null"/>.</returns>
    private static string? ResolveConfirmedTrue(bool? value, string displayText)
    {
        return value == true ? displayText : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
