namespace ArrTags.Providers.Radarr;

/// <summary>
/// The integration-boundary DTO for a Radarr quality revision.
/// </summary>
public sealed class RadarrRevision
{
    /// <summary>
    /// Gets the revision version.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// Gets a value indicating whether the release is a repack.
    /// </summary>
    public bool? IsRepack { get; init; }
}
