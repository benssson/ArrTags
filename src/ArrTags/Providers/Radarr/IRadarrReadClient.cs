using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Providers.Radarr;

/// <summary>
/// The read-only Radarr v3 boundary. Every read is scoped to the client's
/// connection and returns a bounded outcome instead of throwing on provider
/// failures. No method calls a Radarr write endpoint.
/// </summary>
public interface IRadarrReadClient : IArrProviderClient
{
    /// <summary>
    /// Reads the local Radarr movie library. Each returned resource may embed
    /// the current movie file; custom-format fields require the dedicated
    /// movie-file read.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded list of local movies.</returns>
    Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>>> GetMoviesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the fully populated movie file for one movie through the dedicated
    /// <c>moviefile?movieId=</c> endpoint, which includes custom-format and
    /// media-info fields that the embedded list resource omits.
    /// </summary>
    /// <param name="movieId">The Radarr-local movie identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded list of current movie files.</returns>
    Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(int movieId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the fully populated movie files for many movies through the single
    /// dedicated <c>moviefile?movieId=</c> endpoint using its repeatable
    /// <c>movieId</c> selector (ADR-018 clause 4). An empty identifier list
    /// returns an empty result without a provider request.
    /// </summary>
    /// <param name="movieIds">The Radarr-local movie identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded list of current movie files.</returns>
    Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(
        IReadOnlyList<int> movieIds,
        CancellationToken cancellationToken);
}
