namespace ArrTags.Providers.Radarr;

/// <summary>
/// The integration-boundary DTO for a Radarr <c>QualityModel</c>.
/// </summary>
public sealed class RadarrQualityModel
{
    /// <summary>
    /// Gets the quality descriptor.
    /// </summary>
    public RadarrQuality? Quality { get; init; }

    /// <summary>
    /// Gets the quality revision.
    /// </summary>
    public RadarrRevision? Revision { get; init; }
}
