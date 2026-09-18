namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr <c>QualityModel</c>.
/// </summary>
public sealed class SonarrQualityModel
{
    /// <summary>
    /// Gets the actual quality descriptor.
    /// </summary>
    public SonarrQuality? Quality { get; init; }

    /// <summary>
    /// Gets the quality revision.
    /// </summary>
    public SonarrRevision? Revision { get; init; }
}
