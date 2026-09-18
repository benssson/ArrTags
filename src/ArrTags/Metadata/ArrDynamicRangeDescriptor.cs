namespace ArrTags.Metadata;

/// <summary>
/// The canonical dynamic-range descriptor. An absent media-information block
/// yields no descriptor, which means the dynamic range is unknown rather than a
/// confirmed negative.
/// </summary>
public sealed class ArrDynamicRangeDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrDynamicRangeDescriptor"/> class.
    /// </summary>
    /// <param name="kind">The normalized dynamic-range family.</param>
    /// <param name="profile">The reported profile text when known.</param>
    /// <param name="origin">The source of the reported values.</param>
    public ArrDynamicRangeDescriptor(ArrDynamicRangeKind kind, string? profile, ArrMetadataOrigin origin)
    {
        Kind = kind;
        Profile = profile;
        Origin = origin;
    }

    /// <summary>
    /// Gets the normalized dynamic-range family.
    /// </summary>
    public ArrDynamicRangeKind Kind { get; }

    /// <summary>
    /// Gets the reported profile text when known.
    /// </summary>
    public string? Profile { get; }

    /// <summary>
    /// Gets the source of the reported values.
    /// </summary>
    public ArrMetadataOrigin Origin { get; }
}
