namespace ArrTags.Rendering;

/// <summary>
/// The provider-neutral V1 badge selector vocabulary from ADR-009. A selector
/// names a canonical <see cref="Metadata.BadgeMetadata"/> field and never a
/// Sonarr or Radarr DTO path, record identifier, quality profile, or extension
/// value.
/// </summary>
/// <remarks>
/// The numeric values are explicit because a selector is part of the immutable
/// configuration and render fingerprints. Inserting a selector without a new
/// schema version would change existing identities.
/// </remarks>
public enum BadgeSelector
{
    /// <summary>
    /// The actual observed file quality label.
    /// </summary>
    Quality = 0,

    /// <summary>
    /// The normalized resolution display label.
    /// </summary>
    Resolution = 1,

    /// <summary>
    /// The confirmed dynamic-range label, with confirmed Dolby Vision shown as
    /// <c>DV</c>.
    /// </summary>
    DynamicRange = 2,

    /// <summary>
    /// The normalized release source label.
    /// </summary>
    Source = 3,

    /// <summary>
    /// The normalized video codec.
    /// </summary>
    VideoCodec = 4,

    /// <summary>
    /// The composite audio value built from confirmed features, codec, and
    /// channel count.
    /// </summary>
    Audio = 5,

    /// <summary>
    /// One bounded custom metadata value. The selector yields one candidate per
    /// retained value, in canonical order.
    /// </summary>
    CustomBadge = 6,

    /// <summary>
    /// The explicit upgrade-pending status. Only a confirmed <see langword="true"/>
    /// value produces a status candidate.
    /// </summary>
    UpgradePending = 7,
}
