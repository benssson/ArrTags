namespace ArrTags.Providers.Radarr;

/// <summary>
/// The integration-boundary DTO for a Radarr <c>MovieResource</c> response. It
/// is a provider DTO and must not leak past the provider-to-canonical mapping
/// boundary.
/// </summary>
public sealed class RadarrMovieResource
{
    /// <summary>
    /// Gets the Radarr-local movie identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the movie title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the original movie title.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// Gets the production year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the movie status string.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Gets the TMDb identifier.
    /// </summary>
    public int? TmdbId { get; init; }

    /// <summary>
    /// Gets the IMDb identifier.
    /// </summary>
    public string? ImdbId { get; init; }

    /// <summary>
    /// Gets the Radarr quality-profile identifier. It is requested policy, not
    /// actual file quality.
    /// </summary>
    public int? QualityProfileId { get; init; }

    /// <summary>
    /// Gets a value indicating whether Radarr reports an imported file.
    /// </summary>
    public bool? HasFile { get; init; }

    /// <summary>
    /// Gets the current movie-file identifier, or zero when no file exists.
    /// </summary>
    public int? MovieFileId { get; init; }

    /// <summary>
    /// Gets the embedded current movie file, when Radarr includes it.
    /// </summary>
    public RadarrMovieFileResource? MovieFile { get; init; }
}
