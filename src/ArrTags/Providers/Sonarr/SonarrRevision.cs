namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr quality revision.
/// </summary>
public sealed class SonarrRevision
{
    /// <summary>
    /// Gets the revision version.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// Gets the reported real revision number.
    /// </summary>
    public int? Real { get; init; }

    /// <summary>
    /// Gets a value indicating whether the release is a repack.
    /// </summary>
    public bool? IsRepack { get; init; }
}
