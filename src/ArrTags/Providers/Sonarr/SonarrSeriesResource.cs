namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr <c>SeriesResource</c> response. It
/// is a provider DTO and must not leak past the provider-to-canonical mapping
/// boundary. Only the identity and descriptive fields ArrTags consumes are
/// modeled; unknown JSON fields are ignored.
/// </summary>
public sealed class SonarrSeriesResource
{
    /// <summary>
    /// Gets the Sonarr-local series identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the series title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the original series title.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// Gets the production year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the series status string, such as continuing or ended.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Gets the series root folder path on the Sonarr host.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets the TVDB identifier.
    /// </summary>
    public int? TvdbId { get; init; }

    /// <summary>
    /// Gets the TMDb identifier.
    /// </summary>
    public int? TmdbId { get; init; }

    /// <summary>
    /// Gets the TVMaze identifier.
    /// </summary>
    public int? TvMazeId { get; init; }

    /// <summary>
    /// Gets the IMDb identifier.
    /// </summary>
    public string? ImdbId { get; init; }

    /// <summary>
    /// Gets the Sonarr quality-profile identifier. It is requested policy, not
    /// actual file quality.
    /// </summary>
    public int? QualityProfileId { get; init; }

    /// <summary>
    /// Gets the quality-profile display name. It is requested policy, not actual
    /// file quality.
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the series is monitored.
    /// </summary>
    public bool? Monitored { get; init; }
}
