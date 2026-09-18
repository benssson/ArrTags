using System;
using System.Collections.Generic;

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// Resolves the current Sonarr episode file for an episode by the validated
/// <c>episodeFileId == episodeFile.id</c> join. The embedded episode file is
/// trusted only when its identifier matches the episode's authoritative
/// <c>episodeFileId</c>; otherwise the file is resolved from the series file
/// inventory. A missing file identity resolves to <see langword="null"/> rather
/// than to an unrelated file.
/// </summary>
public static class SonarrEpisodeFileResolver
{
    /// <summary>
    /// Resolves the current episode file for one episode.
    /// </summary>
    /// <param name="episode">The episode whose current file is resolved.</param>
    /// <param name="seriesEpisodeFiles">
    /// The candidate file inventory for the episode's series. It may be empty.
    /// </param>
    /// <returns>
    /// The current file when the validated join succeeds; otherwise
    /// <see langword="null"/>, meaning the episode has no current file identity.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="episode"/> or <paramref name="seriesEpisodeFiles"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static SonarrEpisodeFileResource? Resolve(
        SonarrEpisodeResource episode,
        IReadOnlyList<SonarrEpisodeFileResource> seriesEpisodeFiles)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(seriesEpisodeFiles);

        if (episode.EpisodeFileId is not int episodeFileId || episodeFileId <= 0)
        {
            return null;
        }

        if (episode.HasFile == false)
        {
            return null;
        }

        var embedded = episode.EpisodeFile;
        if (embedded is not null && embedded.Id == episodeFileId)
        {
            return embedded;
        }

        foreach (var file in seriesEpisodeFiles)
        {
            if (file is not null && file.Id == episodeFileId)
            {
                return file;
            }
        }

        return null;
    }
}
