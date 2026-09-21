using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using ArrTags.Metadata;

namespace ArrTags.Reconciliation;

/// <summary>
/// A provider-neutral, secret-free, serializable snapshot of the normalized
/// badge metadata for one matched Arr record. It is the persisted form of the
/// canonical <see cref="BadgeMetadata"/> and the last-known-good normalized
/// snapshot the later artwork-invalidation task consumes. Provider DTOs never
/// reach this type.
/// </summary>
public sealed class MetadataSnapshot
{
    private static readonly IReadOnlyList<string> NoCustomBadges = Array.Empty<string>();
    private static readonly IReadOnlyDictionary<string, string> NoExtensions =
        new Dictionary<string, string>(StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataSnapshot"/> class.
    /// </summary>
    /// <param name="badgeSchemaVersion">The normalized metadata shape version.</param>
    /// <param name="qualityLabel">The actual observed file quality label when reported.</param>
    /// <param name="qualitySource">The actual observed file quality source when reported.</param>
    /// <param name="qualityResolution">The actual observed file quality resolution when reported.</param>
    /// <param name="qualityModifier">The actual observed file quality modifier when reported.</param>
    /// <param name="qualityProviderId">The provider-local quality identifier when reported.</param>
    /// <param name="resolutionWidth">The inspected media width when reported.</param>
    /// <param name="resolutionHeight">The inspected media height when reported.</param>
    /// <param name="resolutionLabel">The reported resolution label when known.</param>
    /// <param name="resolutionOrigin">The origin of the resolution values when a descriptor exists.</param>
    /// <param name="dynamicRangeKind">The normalized dynamic-range family when reported.</param>
    /// <param name="dynamicRangeProfile">The reported dynamic-range profile when known.</param>
    /// <param name="dynamicRangeOrigin">The origin of the dynamic-range values when a descriptor exists.</param>
    /// <param name="dolbyVision">The tri-state Dolby Vision observation.</param>
    /// <param name="videoCodec">The normalized video codec when reported.</param>
    /// <param name="audioCodec">The normalized audio codec when reported.</param>
    /// <param name="audioChannels">The audio channel count when reported.</param>
    /// <param name="audioFeatures">The derived audio feature set.</param>
    /// <param name="source">The normalized release source when reported.</param>
    /// <param name="upgradePending">The tri-state upgrade-pending signal.</param>
    /// <param name="customBadges">The ordered, bounded custom metadata values.</param>
    /// <param name="extensions">The namespaced extension values.</param>
    public MetadataSnapshot(
        int badgeSchemaVersion,
        string? qualityLabel = null,
        string? qualitySource = null,
        int? qualityResolution = null,
        string? qualityModifier = null,
        int? qualityProviderId = null,
        int? resolutionWidth = null,
        int? resolutionHeight = null,
        string? resolutionLabel = null,
        ArrMetadataOrigin? resolutionOrigin = null,
        ArrDynamicRangeKind? dynamicRangeKind = null,
        string? dynamicRangeProfile = null,
        ArrMetadataOrigin? dynamicRangeOrigin = null,
        bool? dolbyVision = null,
        string? videoCodec = null,
        string? audioCodec = null,
        double? audioChannels = null,
        IReadOnlyList<ArrAudioFeature>? audioFeatures = null,
        string? source = null,
        bool? upgradePending = null,
        IReadOnlyList<string>? customBadges = null,
        IReadOnlyDictionary<string, string>? extensions = null)
    {
        BadgeSchemaVersion = badgeSchemaVersion;
        QualityLabel = qualityLabel;
        QualitySource = qualitySource;
        QualityResolution = qualityResolution;
        QualityModifier = qualityModifier;
        QualityProviderId = qualityProviderId;
        ResolutionWidth = resolutionWidth;
        ResolutionHeight = resolutionHeight;
        ResolutionLabel = resolutionLabel;
        ResolutionOrigin = resolutionOrigin;
        DynamicRangeKind = dynamicRangeKind;
        DynamicRangeProfile = dynamicRangeProfile;
        DynamicRangeOrigin = dynamicRangeOrigin;
        DolbyVision = dolbyVision;
        VideoCodec = videoCodec;
        AudioCodec = audioCodec;
        AudioChannels = audioChannels;
        AudioFeatures = audioFeatures is null ? null : new List<ArrAudioFeature>(audioFeatures).AsReadOnly();
        Source = source;
        UpgradePending = upgradePending;
        CustomBadges = customBadges is null ? NoCustomBadges : new List<string>(customBadges).AsReadOnly();
        Extensions = extensions is null ? NoExtensions : new Dictionary<string, string>(extensions, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the normalized metadata shape version.
    /// </summary>
    public int BadgeSchemaVersion { get; }

    /// <summary>
    /// Gets the actual observed file quality label when reported.
    /// </summary>
    public string? QualityLabel { get; }

    /// <summary>
    /// Gets the actual observed file quality source when reported.
    /// </summary>
    public string? QualitySource { get; }

    /// <summary>
    /// Gets the actual observed file quality resolution when reported.
    /// </summary>
    public int? QualityResolution { get; }

    /// <summary>
    /// Gets the actual observed file quality modifier when reported.
    /// </summary>
    public string? QualityModifier { get; }

    /// <summary>
    /// Gets the provider-local quality identifier when reported.
    /// </summary>
    public int? QualityProviderId { get; }

    /// <summary>
    /// Gets the inspected media width when reported.
    /// </summary>
    public int? ResolutionWidth { get; }

    /// <summary>
    /// Gets the inspected media height when reported.
    /// </summary>
    public int? ResolutionHeight { get; }

    /// <summary>
    /// Gets the reported resolution label when known.
    /// </summary>
    public string? ResolutionLabel { get; }

    /// <summary>
    /// Gets the origin of the resolution values when a descriptor exists.
    /// </summary>
    public ArrMetadataOrigin? ResolutionOrigin { get; }

    /// <summary>
    /// Gets the normalized dynamic-range family when reported.
    /// </summary>
    public ArrDynamicRangeKind? DynamicRangeKind { get; }

    /// <summary>
    /// Gets the reported dynamic-range profile when known.
    /// </summary>
    public string? DynamicRangeProfile { get; }

    /// <summary>
    /// Gets the origin of the dynamic-range values when a descriptor exists.
    /// </summary>
    public ArrMetadataOrigin? DynamicRangeOrigin { get; }

    /// <summary>
    /// Gets the tri-state Dolby Vision observation. <see langword="null"/> means
    /// unknown, not false.
    /// </summary>
    public bool? DolbyVision { get; }

    /// <summary>
    /// Gets the normalized video codec when reported.
    /// </summary>
    public string? VideoCodec { get; }

    /// <summary>
    /// Gets the normalized audio codec when reported.
    /// </summary>
    public string? AudioCodec { get; }

    /// <summary>
    /// Gets the audio channel count when reported.
    /// </summary>
    public double? AudioChannels { get; }

    /// <summary>
    /// Gets the derived audio feature set. <see langword="null"/> means the
    /// source did not report usable audio codec data (unknown); an empty list
    /// means a codec was reported but no known feature was detected.
    /// </summary>
    public IReadOnlyList<ArrAudioFeature>? AudioFeatures { get; }

    /// <summary>
    /// Gets the normalized release source when reported.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Gets the tri-state upgrade-pending signal. <see langword="null"/> means
    /// unknown, not false.
    /// </summary>
    public bool? UpgradePending { get; }

    /// <summary>
    /// Gets the ordered, bounded custom metadata values.
    /// </summary>
    public IReadOnlyList<string> CustomBadges { get; }

    /// <summary>
    /// Gets the namespaced extension values.
    /// </summary>
    public IReadOnlyDictionary<string, string> Extensions { get; }

    /// <summary>
    /// Creates the serializable snapshot from canonical badge metadata.
    /// </summary>
    /// <param name="metadata">The canonical metadata to snapshot.</param>
    /// <returns>The serializable snapshot.</returns>
    /// <exception cref="ArgumentNullException">The metadata is <see langword="null"/>.</exception>
    public static MetadataSnapshot From(BadgeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var resolution = metadata.Resolution;
        var dynamicRange = metadata.DynamicRange;

        return new MetadataSnapshot(
            metadata.BadgeSchemaVersion,
            qualityLabel: metadata.Quality?.Label,
            qualitySource: metadata.Quality?.Source,
            qualityResolution: metadata.Quality?.Resolution,
            qualityModifier: metadata.Quality?.Modifier,
            qualityProviderId: metadata.Quality?.ProviderQualityId,
            resolutionWidth: resolution?.Width,
            resolutionHeight: resolution?.Height,
            resolutionLabel: resolution?.Label,
            resolutionOrigin: resolution?.Origin,
            dynamicRangeKind: dynamicRange?.Kind,
            dynamicRangeProfile: dynamicRange?.Profile,
            dynamicRangeOrigin: dynamicRange?.Origin,
            dolbyVision: metadata.DolbyVision,
            videoCodec: metadata.VideoCodec,
            audioCodec: metadata.AudioCodec,
            audioChannels: metadata.AudioChannels,
            audioFeatures: metadata.AudioFeatures is null ? null : new List<ArrAudioFeature>(metadata.AudioFeatures),
            source: metadata.Source,
            upgradePending: metadata.UpgradePending,
            customBadges: metadata.CustomBadges,
            extensions: metadata.Extensions);
    }

    /// <summary>
    /// Validates the snapshot field invariants. A snapshot is a bounded,
    /// normalized shape and must not carry an unknown schema version or an
    /// unbounded custom collection.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation when invalid.</param>
    /// <returns><see langword="true"/> when the snapshot is valid.</returns>
    public bool Validate(out string reason)
    {
        if (BadgeSchemaVersion <= 0)
        {
            reason = "The metadata snapshot schema version must be positive.";
            return false;
        }

        if (CustomBadges.Count > BadgeMetadata.MaxCustomBadgeCount)
        {
            reason = "The metadata snapshot carries more than the bounded custom-value count.";
            return false;
        }

        foreach (var badge in CustomBadges)
        {
            if (badge is null || badge.Length > BadgeMetadata.MaxCustomBadgeLength)
            {
                reason = "The metadata snapshot carries an out-of-range custom value.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }
}
