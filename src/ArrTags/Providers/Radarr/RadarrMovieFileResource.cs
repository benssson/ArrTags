using System.Collections.Generic;

namespace ArrTags.Providers.Radarr;

/// <summary>
/// The integration-boundary DTO for a Radarr <c>MovieFileResource</c> response.
/// Actual file quality and technical values come from this resource; custom
/// format fields are populated only by the dedicated movie-file endpoint.
/// </summary>
public sealed class RadarrMovieFileResource
{
    /// <summary>
    /// Gets the Radarr-local movie-file identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the owning Radarr movie identifier.
    /// </summary>
    public int? MovieId { get; init; }

    /// <summary>
    /// Gets the file path relative to the movie folder.
    /// </summary>
    public string? RelativePath { get; init; }

    /// <summary>
    /// Gets the reported file path.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// Gets the release group.
    /// </summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>
    /// Gets the edition label.
    /// </summary>
    public string? Edition { get; init; }

    /// <summary>
    /// Gets the scene release name.
    /// </summary>
    public string? SceneName { get; init; }

    /// <summary>
    /// Gets the actual observed file quality.
    /// </summary>
    public RadarrQualityModel? Quality { get; init; }

    /// <summary>
    /// Gets the matched custom formats. Present only from the dedicated
    /// movie-file endpoint.
    /// </summary>
    public IReadOnlyList<RadarrCustomFormatResource>? CustomFormats { get; init; }

    /// <summary>
    /// Gets the summed custom-format score. Present only from the dedicated
    /// movie-file endpoint.
    /// </summary>
    public int? CustomFormatScore { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file has not reached the profile
    /// cutoff and an upgrade is still desirable.
    /// </summary>
    public bool? QualityCutoffNotMet { get; init; }

    /// <summary>
    /// Gets the ffprobe-derived technical media information.
    /// </summary>
    public RadarrMediaInfoResource? MediaInfo { get; init; }
}
