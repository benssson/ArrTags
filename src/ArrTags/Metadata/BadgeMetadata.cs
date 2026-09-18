using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ArrTags.Providers;

namespace ArrTags.Metadata;

/// <summary>
/// The provider-agnostic, badge-relevant observation for the current matched
/// media file. It is the renderer's metadata input and is produced by mapping a
/// validated provider file resource at the integration boundary. Each optional
/// technical value supports an explicit unknown state; a missing value is never
/// presented as a confirmed negative.
/// </summary>
public sealed class BadgeMetadata
{
    /// <summary>
    /// The current normalized metadata shape version. Version 2 made the audio
    /// feature set nullable so an unreported set is unknown rather than an empty
    /// confirmed value.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// The maximum number of provider custom badge values retained in canonical
    /// metadata. Excess values are dropped in provider order so an unbounded
    /// provider response cannot flow into a badge or its fingerprint.
    /// </summary>
    public const int MaxCustomBadgeCount = 32;

    /// <summary>
    /// The maximum length of a single provider custom badge value. Longer
    /// values are truncated so one value cannot dominate a badge. This is a
    /// defensive metadata bound, not the renderer's display or truncation policy.
    /// </summary>
    public const int MaxCustomBadgeLength = 128;

    private static readonly IReadOnlyList<string> NoCustomBadges =
        Array.Empty<string>();

    private static readonly IReadOnlyDictionary<string, string> NoExtensions =
        new Dictionary<string, string>(StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="BadgeMetadata"/> class and
    /// computes its metadata fingerprint.
    /// </summary>
    /// <param name="provider">The provider identity that supplied the observation.</param>
    /// <param name="recordIdentity">The connection-scoped record and file identity.</param>
    /// <param name="observedAt">When the source observation was obtained.</param>
    /// <param name="quality">The actual observed file quality.</param>
    /// <param name="resolution">The normalized resolution.</param>
    /// <param name="dynamicRange">The normalized dynamic range.</param>
    /// <param name="dolbyVision">The tri-state Dolby Vision observation.</param>
    /// <param name="videoCodec">The normalized video codec.</param>
    /// <param name="audioCodec">The normalized audio codec.</param>
    /// <param name="audioChannels">The channel count.</param>
    /// <param name="audioFeatures">The derived audio feature set.</param>
    /// <param name="source">The normalized release source.</param>
    /// <param name="upgradePending">The tri-state upgrade-pending policy signal.</param>
    /// <param name="customBadges">The ordered custom metadata values.</param>
    /// <param name="extensions">The namespaced extension values.</param>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The record identity provider kind does not match the provider.</exception>
    public BadgeMetadata(
        ArrProvider provider,
        ArrRecordIdentity recordIdentity,
        DateTimeOffset observedAt,
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
        IEnumerable<string>? customBadges = null,
        IReadOnlyDictionary<string, string>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(recordIdentity);

        if (provider.Kind != recordIdentity.ProviderKind)
        {
            throw new ArgumentException(
                "The record identity provider kind must match the provider identity.",
                nameof(recordIdentity));
        }

        Provider = provider;
        RecordIdentity = recordIdentity;
        ObservedAt = observedAt;
        Quality = quality;
        Resolution = resolution;
        DynamicRange = dynamicRange;
        DolbyVision = dolbyVision;
        VideoCodec = videoCodec;
        AudioCodec = audioCodec;
        AudioChannels = audioChannels;
        Source = source;
        UpgradePending = upgradePending;
        AudioFeatures = audioFeatures?.ToFrozenSet();
        CustomBadges = customBadges is null
            ? NoCustomBadges
            : BoundCustomBadges(customBadges);
        Extensions = extensions is null
            ? NoExtensions
            : extensions.ToFrozenDictionary(StringComparer.Ordinal);
        BadgeSchemaVersion = CurrentSchemaVersion;
        MetadataFingerprint = ComputeFingerprint();
    }

    /// <summary>
    /// Gets the normalized metadata shape version.
    /// </summary>
    public int BadgeSchemaVersion { get; }

    /// <summary>
    /// Gets the provider identity that supplied the observation.
    /// </summary>
    public ArrProvider Provider { get; }

    /// <summary>
    /// Gets the connection-scoped record and file identity.
    /// </summary>
    public ArrRecordIdentity RecordIdentity { get; }

    /// <summary>
    /// Gets when the source observation was obtained.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>
    /// Gets the actual observed file quality.
    /// </summary>
    public ArrQualityDescriptor? Quality { get; }

    /// <summary>
    /// Gets the normalized resolution.
    /// </summary>
    public ArrResolutionDescriptor? Resolution { get; }

    /// <summary>
    /// Gets the normalized dynamic range.
    /// </summary>
    public ArrDynamicRangeDescriptor? DynamicRange { get; }

    /// <summary>
    /// Gets the tri-state Dolby Vision observation. <see langword="null"/> means
    /// unknown, not false.
    /// </summary>
    public bool? DolbyVision { get; }

    /// <summary>
    /// Gets the normalized video codec.
    /// </summary>
    public string? VideoCodec { get; }

    /// <summary>
    /// Gets the normalized audio codec.
    /// </summary>
    public string? AudioCodec { get; }

    /// <summary>
    /// Gets the audio channel count.
    /// </summary>
    public double? AudioChannels { get; }

    /// <summary>
    /// Gets the derived audio feature set. <see langword="null"/> means the
    /// source did not report usable audio codec data and the feature state is
    /// unknown; an empty set means the source reported a codec for which no
    /// known feature was detected.
    /// </summary>
    public IReadOnlySet<ArrAudioFeature>? AudioFeatures { get; }

    /// <summary>
    /// Gets the normalized release source.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Gets the tri-state upgrade-pending policy signal. <see langword="null"/>
    /// means unknown, not false.
    /// </summary>
    public bool? UpgradePending { get; }

    /// <summary>
    /// Gets the ordered, bounded custom metadata values. Blank values and
    /// control characters are removed; at most
    /// <see cref="MaxCustomBadgeCount"/> values of at most
    /// <see cref="MaxCustomBadgeLength"/> characters are retained.
    /// </summary>
    public IReadOnlyList<string> CustomBadges { get; }

    /// <summary>
    /// Gets the namespaced extension values.
    /// </summary>
    public IReadOnlyDictionary<string, string> Extensions { get; }

    /// <summary>
    /// Gets the deterministic fingerprint over the badge schema version, the
    /// provider and record identity, and every badge-affecting normalized value.
    /// It excludes the observation timestamp and never contains a credential.
    /// </summary>
    public string MetadataFingerprint { get; }

    private string ComputeFingerprint()
    {
        var builder = new StringBuilder();
        Append(builder, "badgeSchemaVersion", BadgeSchemaVersion.ToString(CultureInfo.InvariantCulture));
        Append(builder, "providerKind", Provider.Kind.ToApiName());
        Append(builder, "providerInstanceId", Provider.ProviderInstanceId);
        Append(builder, "recordIdentity", RecordIdentity.ToString());
        Append(builder, "quality", DescribeQuality(Quality));
        Append(builder, "resolution", DescribeResolution(Resolution));
        Append(builder, "dynamicRange", DescribeDynamicRange(DynamicRange));
        Append(builder, "dolbyVision", DescribeTriState(DolbyVision));
        Append(builder, "videoCodec", VideoCodec);
        Append(builder, "audioCodec", AudioCodec);
        Append(builder, "audioChannels", AudioChannels?.ToString(CultureInfo.InvariantCulture));
        Append(builder, "audioFeatures", DescribeAudioFeatures(AudioFeatures));
        Append(builder, "source", Source);
        Append(builder, "upgradePending", DescribeTriState(UpgradePending));
        Append(builder, "customBadges", string.Join(",", CustomBadges));
        Append(builder, "extensions", string.Join(
            ";",
            Extensions
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value)));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append('=').Append(value ?? string.Empty).Append('\n');
    }

    private static string? DescribeQuality(ArrQualityDescriptor? quality)
    {
        return quality is null
            ? null
            : string.Join(
                "|",
                quality.Label ?? string.Empty,
                quality.Source ?? string.Empty,
                quality.Resolution?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                quality.Modifier ?? string.Empty,
                quality.ProviderQualityId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string? DescribeResolution(ArrResolutionDescriptor? resolution)
    {
        return resolution is null
            ? null
            : string.Join(
                "|",
                resolution.Width?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                resolution.Height?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                resolution.Label ?? string.Empty,
                resolution.Origin.ToString());
    }

    private static string? DescribeDynamicRange(ArrDynamicRangeDescriptor? dynamicRange)
    {
        return dynamicRange is null
            ? null
            : string.Join(
                "|",
                dynamicRange.Kind.ToString(),
                dynamicRange.Profile ?? string.Empty,
                dynamicRange.Origin.ToString());
    }

    private static string DescribeTriState(bool? value)
    {
        return value is null ? "unknown" : value.Value ? "true" : "false";
    }

    private static string DescribeAudioFeatures(IReadOnlySet<ArrAudioFeature>? audioFeatures)
    {
        return audioFeatures is null
            ? "unknown"
            : string.Join(",", audioFeatures.OrderBy(feature => feature));
    }

    private static IReadOnlyList<string> BoundCustomBadges(IEnumerable<string> customBadges)
    {
        var bounded = new List<string>();
        foreach (var badge in customBadges)
        {
            if (bounded.Count >= MaxCustomBadgeCount)
            {
                break;
            }

            var value = SanitizeBadge(badge);
            if (value is null)
            {
                continue;
            }

            bounded.Add(value.Length <= MaxCustomBadgeLength
                ? value
                : Truncate(value, MaxCustomBadgeLength));
        }

        return bounded.AsReadOnly();
    }

    private static string? SanitizeBadge(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? null : sanitized;
    }

    private static string Truncate(string value, int maxLength)
    {
        var length = maxLength;
        if (length < value.Length
            && char.IsHighSurrogate(value[length - 1])
            && char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        return value[..length];
    }
}
