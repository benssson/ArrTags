namespace ArrTags.Metadata;

/// <summary>
/// The canonical actual-file quality descriptor. It is normalized from the
/// current Arr file's quality model and never from a quality profile, which
/// describes requested policy rather than observed quality.
/// </summary>
public sealed class ArrQualityDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrQualityDescriptor"/> class.
    /// </summary>
    /// <param name="label">The human-facing quality label when reported.</param>
    /// <param name="source">The normalized quality source when reported.</param>
    /// <param name="resolution">The numeric resolution when reported.</param>
    /// <param name="modifier">The quality modifier when reported.</param>
    /// <param name="providerQualityId">The provider-local quality identifier.</param>
    public ArrQualityDescriptor(
        string? label,
        string? source,
        int? resolution,
        string? modifier,
        int? providerQualityId)
    {
        Label = label;
        Source = source;
        Resolution = resolution;
        Modifier = modifier;
        ProviderQualityId = providerQualityId;
    }

    /// <summary>
    /// Gets the human-facing quality label when reported.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Gets the normalized quality source when reported.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Gets the numeric resolution when reported.
    /// </summary>
    public int? Resolution { get; }

    /// <summary>
    /// Gets the quality modifier when reported.
    /// </summary>
    public string? Modifier { get; }

    /// <summary>
    /// Gets the provider-local quality identifier. It is stable only within one
    /// connection and is never used as a cross-provider identity.
    /// </summary>
    public int? ProviderQualityId { get; }
}
