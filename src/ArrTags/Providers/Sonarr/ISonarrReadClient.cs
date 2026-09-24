using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The read-only Sonarr v3 boundary. Every read is scoped to the client's
/// connection and returns a bounded outcome instead of throwing on provider
/// failures. No method calls a Sonarr write endpoint.
/// </summary>
public interface ISonarrReadClient : IArrProviderClient
{
    /// <summary>
    /// Reads the local Sonarr series library.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded list of local series.</returns>
    Task<ArrProviderReadResult<IReadOnlyList<SonarrSeriesResource>>> GetSeriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the episodes for one series, including the embedded current episode
    /// file when Sonarr includes it. The embedded file is trusted only when its
    /// identifier matches <c>episodeFileId</c>; use
    /// <see cref="SonarrEpisodeFileResolver.Resolve"/> with the series file
    /// inventory otherwise.
    /// </summary>
    /// <param name="seriesId">The Sonarr-local series identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded list of episodes.</returns>
    Task<ArrProviderReadResult<IReadOnlyList<SonarrEpisodeResource>>> GetEpisodesAsync(int seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current episode-file inventory for one series. A series query is
    /// an inventory, not proof that each file is current for an episode; resolve
    /// the current file with
    /// <see cref="SonarrEpisodeFileResolver.Resolve"/>.
    /// </summary>
    /// <param name="seriesId">The Sonarr-local series identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded list of episode files.</returns>
    Task<ArrProviderReadResult<IReadOnlyList<SonarrEpisodeFileResource>>> GetEpisodeFilesAsync(int seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads many episode files through the single dedicated
    /// <c>episodeFile?episodeFileIds=</c> endpoint using its repeatable
    /// <c>episodeFileIds</c> selector (ADR-018 clause 4). An empty identifier
    /// list returns an empty result without a provider request.
    /// </summary>
    /// <param name="episodeFileIds">The Sonarr-local episode-file identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded list of episode files.</returns>
    Task<ArrProviderReadResult<IReadOnlyList<SonarrEpisodeFileResource>>> GetEpisodeFilesAsync(
        IReadOnlyList<int> episodeFileIds,
        CancellationToken cancellationToken);
}
