namespace ArrTags.Metadata;

/// <summary>
/// Records where a normalized badge value came from. Origin is retained so a
/// renderer can distinguish inspected media information from a provider quality
/// classification or a derived value without re-reading the provider DTO.
/// </summary>
public enum ArrMetadataOrigin
{
    /// <summary>
    /// The source of the value is not known.
    /// </summary>
    Unknown,

    /// <summary>
    /// The value came from the provider's actual file quality classification.
    /// </summary>
    ProviderQuality,

    /// <summary>
    /// The value came from the provider's inspected media information.
    /// </summary>
    ProviderMediaInfo,

    /// <summary>
    /// The value came from Jellyfin's own stream analysis.
    /// </summary>
    JellyfinStream,

    /// <summary>
    /// The value was derived or normalized by ArrTags from other reported values.
    /// </summary>
    Derived,
}
