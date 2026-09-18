using System;

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr <c>EpisodeResource</c> response.
/// <see cref="EpisodeFileId"/> is the authoritative current-file association;
/// <see cref="HasFile"/> is only a convenience flag and
/// <see cref="EpisodeFile"/> is trusted only when its identifier matches.
/// </summary>
public sealed class SonarrEpisodeResource
{
    /// <summary>
    /// Gets the Sonarr-local episode identifier. It is not a TVDB identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the owning Sonarr-local series identifier.
    /// </summary>
    public int SeriesId { get; init; }

    /// <summary>
    /// Gets the episode TVDB identifier when Sonarr reports it.
    /// </summary>
    public int? TvdbId { get; init; }

    /// <summary>
    /// Gets the episode title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the season number. Season zero represents specials where applicable.
    /// </summary>
    public int? SeasonNumber { get; init; }

    /// <summary>
    /// Gets the episode number within the season.
    /// </summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>
    /// Gets the absolute episode number when Sonarr reports it.
    /// </summary>
    public int? AbsoluteEpisodeNumber { get; init; }

    /// <summary>
    /// Gets the scene absolute episode number when Sonarr reports it.
    /// </summary>
    public int? SceneAbsoluteEpisodeNumber { get; init; }

    /// <summary>
    /// Gets the scene season number when Sonarr reports it.
    /// </summary>
    public int? SceneSeasonNumber { get; init; }

    /// <summary>
    /// Gets the scene episode number when Sonarr reports it.
    /// </summary>
    public int? SceneEpisodeNumber { get; init; }

    /// <summary>
    /// Gets the current episode-file identifier, or zero when no file is
    /// associated. It is the authoritative current-file association.
    /// </summary>
    public int? EpisodeFileId { get; init; }

    /// <summary>
    /// Gets a value indicating whether Sonarr reports an imported file. It is a
    /// convenience flag only; use <see cref="EpisodeFileId"/> for the join.
    /// </summary>
    public bool? HasFile { get; init; }

    /// <summary>
    /// Gets a value indicating whether the episode is monitored.
    /// </summary>
    public bool? Monitored { get; init; }

    /// <summary>
    /// Gets the original air date when Sonarr reports it.
    /// </summary>
    public DateTimeOffset? AirDateUtc { get; init; }

    /// <summary>
    /// Gets the embedded current episode file when Sonarr includes it. It is
    /// trusted only when its identifier matches <see cref="EpisodeFileId"/>.
    /// </summary>
    public SonarrEpisodeFileResource? EpisodeFile { get; init; }
}
