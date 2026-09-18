using System;
using System.Collections.Generic;

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr <c>EpisodeFileResource</c>
/// response. Actual file quality and technical values come from this resource;
/// custom-format fields are present only from the episode-file endpoints.
/// </summary>
public sealed class SonarrEpisodeFileResource
{
    /// <summary>
    /// Gets the Sonarr-local episode-file identifier.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the owning Sonarr-local series identifier.
    /// </summary>
    public int? SeriesId { get; init; }

    /// <summary>
    /// Gets the file path relative to the series folder.
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
    /// Gets the time the file was added to Sonarr.
    /// </summary>
    public DateTimeOffset? DateAdded { get; init; }

    /// <summary>
    /// Gets the release group.
    /// </summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>
    /// Gets the scene release name.
    /// </summary>
    public string? SceneName { get; init; }

    /// <summary>
    /// Gets the release type when Sonarr reports it.
    /// </summary>
    public string? ReleaseType { get; init; }

    /// <summary>
    /// Gets the actual observed file quality. It is never the series quality
    /// profile.
    /// </summary>
    public SonarrQualityModel? Quality { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file has not reached the profile
    /// cutoff and an upgrade is still desirable.
    /// </summary>
    public bool? QualityCutoffNotMet { get; init; }

    /// <summary>
    /// Gets the matched custom formats. Present only from the episode-file
    /// endpoints.
    /// </summary>
    public IReadOnlyList<SonarrCustomFormatResource>? CustomFormats { get; init; }

    /// <summary>
    /// Gets the summed custom-format score. Present only from the episode-file
    /// endpoints.
    /// </summary>
    public int? CustomFormatScore { get; init; }

    /// <summary>
    /// Gets the ffprobe-derived technical media information.
    /// </summary>
    public SonarrMediaInfoResource? MediaInfo { get; init; }

    /// <summary>
    /// Gets the reported file languages.
    /// </summary>
    public IReadOnlyList<SonarrLanguageResource>? Languages { get; init; }
}
