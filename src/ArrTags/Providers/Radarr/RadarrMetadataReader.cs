using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Logging;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Reconciliation;
using Microsoft.Extensions.Logging;

namespace ArrTags.Providers.Radarr;

/// <summary>
/// The reconciliation reader for Radarr. It composes the read-only Radarr client,
/// the canonical movie-candidate assembly, the documented movie matching order,
/// and the canonical metadata mapping. The Radarr DTOs consumed here never leave
/// this provider layer; the returned match and metadata are provider-neutral.
/// </summary>
/// <remarks>
/// When the bounded provider inventory cache (ADR-018) is available, one
/// <c>api/v3/movie</c> library read per connection is reused by every work item
/// in the cache window, and the per-movie file resources are read once through
/// the repeatable <c>moviefile?movieId=</c> selector. A cache hit serves the
/// canonical observations without calling the provider; concurrent cold readers
/// for the same connection serialize through the cache provider's per-connection
/// single-flight gate so one library read serves all of them. An observation set
/// that exceeds the configured record or byte bound, or a bulk file read that
/// fails, keeps the existing direct read unchanged. The cache is never
/// authoritative: a missing or expired set is re-read from the provider, and no
/// provider conditional request or revision token is used.
/// </remarks>
public sealed class RadarrMetadataReader : IArrMetadataReader
{
    private readonly IArrReadClientFactory _clients;
    private readonly IArrTagsLog<RadarrMetadataReader>? _log;
    private readonly ArrInventoryCacheProvider? _inventory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RadarrMetadataReader"/> class.
    /// </summary>
    /// <param name="clients">The connection-scoped read client factory.</param>
    /// <param name="log">The optional bounded, secret-free matching-boundary log.</param>
    /// <param name="inventory">The optional bounded provider inventory cache (ADR-018); when absent the reader keeps the direct provider read.</param>
    /// <exception cref="ArgumentNullException">The factory is <see langword="null"/>.</exception>
    public RadarrMetadataReader(
        IArrReadClientFactory clients,
        IArrTagsLog<RadarrMetadataReader>? log = null,
        ArrInventoryCacheProvider? inventory = null)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _log = log;
        _inventory = inventory;
    }

    /// <inheritdoc />
    public ArrProviderKind Kind => ArrProviderKind.Radarr;

    /// <inheritdoc />
    public async Task<ArrMetadataReadResult> ReadAsync(
        MediaIdentity identity,
        ArrConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        var client = _clients.CreateRadarr(connection);
        var cache = _inventory?.Current;

        if (cache is null)
        {
            var libraryResult = await client.GetMoviesAsync(cancellationToken).ConfigureAwait(false);
            if (!libraryResult.IsSuccess || libraryResult.Value is null)
            {
                return ArrMetadataReadResult.Failure(libraryResult.Error!);
            }

            return await ReadDirectAsync(identity, connection, libraryResult.Value!, client, cancellationToken).ConfigureAwait(false);
        }

        if (cache.TryGet(connection.ConnectionId, DateTimeOffset.UtcNow, out var cached) && cached is not null)
        {
            // A fresh or bounded last-known-good observation set serves every
            // work item in the window without a provider library read.
            return ReadFromObservations(identity, connection, cached.Records);
        }

        // A cold cache: one work item per connection populates it while the
        // others wait on the per-connection gate and then re-check the cache.
        InventoryPopulation population;
        using (var lease = await _inventory!.AcquirePopulationAsync(connection.ConnectionId, cancellationToken).ConfigureAwait(false))
        {
            if (cache.TryGet(connection.ConnectionId, DateTimeOffset.UtcNow, out cached) && cached is not null)
            {
                return ReadFromObservations(identity, connection, cached.Records);
            }

            population = await PopulateAsync(connection, client, cache, cancellationToken).ConfigureAwait(false);
        }

        if (population.Error is not null)
        {
            return ArrMetadataReadResult.Failure(population.Error);
        }

        if (population.Observations is not null)
        {
            return ReadFromObservations(identity, connection, population.Observations);
        }

        // The set is over the configured bound or the bulk read failed: keep the
        // existing direct read unchanged, reusing the library read already made.
        return await ReadDirectAsync(identity, connection, population.Movies!, client, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Matches and maps one item from a canonical observation set without any
    /// provider call. The matched record's file observation is the cached
    /// canonical metadata; an absent file yields a matched state with an
    /// explicit absent-file observation.
    /// </summary>
    private ArrMetadataReadResult ReadFromObservations(
        MediaIdentity identity,
        ArrConnection connection,
        IReadOnlyList<ArrInventoryRecordObservation> observations)
    {
        var candidates = new List<MatchCandidate>(observations.Count);
        foreach (var observation in observations)
        {
            candidates.Add(observation.Candidate);
        }

        var match = MediaMatcher.Match(identity, connection.Provider, connection.ConnectionId, candidates);
        LogMatch(identity, connection, match);
        if (match.Status != MediaMatchStatus.Matched || match.RecordIdentity is not RadarrIdentity record)
        {
            return ArrMetadataReadResult.Success(match);
        }

        foreach (var observation in observations)
        {
            if (observation.Candidate.RecordIdentity is RadarrIdentity candidateRecord
                && candidateRecord.Equals(record))
            {
                // A record without an imported file carries no cached file
                // observation. The direct read maps an explicit absent-file
                // observation with unknown values, so the cached path preserves
                // the same matched-with-metadata outcome.
                var metadata = observation.FileObservation
                    ?? new BadgeMetadata(connection.Provider, record, DateTimeOffset.UtcNow);
                return ArrMetadataReadResult.Success(match, metadata);
            }
        }

        // The matched record is not present in the observation set. The bounded
        // outcome is a matched state without metadata rather than a guessed
        // snapshot.
        return ArrMetadataReadResult.Success(match);
    }

    /// <summary>
    /// Reads the movie library once and builds the canonical observation set from
    /// one bounded bulk movie-file read (chunked by the client when the id list is
    /// large). It reports a library failure, an over-bound/failed-bulk fallback,
    /// or a stored set so the caller can serve the observations or keep the
    /// existing direct read unchanged.
    /// </summary>
    private async Task<InventoryPopulation> PopulateAsync(
        ArrConnection connection,
        IRadarrReadClient client,
        ArrInventoryCache cache,
        CancellationToken cancellationToken)
    {
        var libraryResult = await client.GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        if (!libraryResult.IsSuccess || libraryResult.Value is null)
        {
            return InventoryPopulation.Failed(libraryResult.Error!);
        }

        IReadOnlyList<RadarrMovieResource> movies = libraryResult.Value!;

        // One observation per movie, so the record bound is known before the
        // bulk file read.
        if (movies.Count > cache.MaxRecordsPerConnection)
        {
            return InventoryPopulation.NotStored(movies);
        }

        var observedAt = DateTimeOffset.UtcNow;
        var movieIdsWithFiles = new List<int>();
        try
        {
            foreach (var movie in movies)
            {
                if (RadarrMetadataMapper.MapIdentity(connection, movie).MovieFileIdentity.Presence == ArrFilePresence.Present)
                {
                    movieIdsWithFiles.Add(movie.Id);
                }
            }
        }
        catch (ArgumentException)
        {
            return InventoryPopulation.NotStored(movies);
        }

        var filesById = new Dictionary<int, RadarrMovieFileResource>();
        if (movieIdsWithFiles.Count > 0)
        {
            var filesResult = await client.GetMovieFilesAsync(movieIdsWithFiles, cancellationToken).ConfigureAwait(false);
            if (!filesResult.IsSuccess || filesResult.Value is null)
            {
                // The bulk read is an optimization; a bounded failure keeps the
                // direct read instead of failing the work.
                return InventoryPopulation.NotStored(movies);
            }

            foreach (var file in filesResult.Value!)
            {
                if (file is not null)
                {
                    filesById[file.Id] = file;
                }
            }
        }

        var observations = new List<ArrInventoryRecordObservation>(movies.Count);
        try
        {
            foreach (var movie in movies)
            {
                var candidate = RadarrMatchCandidateFactory.FromMovie(connection, movie);
                BadgeMetadata? fileObservation = null;
                if (candidate.RecordIdentity is RadarrIdentity identity
                    && identity.MovieFileIdentity is { Presence: ArrFilePresence.Present, FileId: int fileId })
                {
                    filesById.TryGetValue(fileId, out var file);
                    fileObservation = RadarrMetadataMapper.Map(connection, movie, file, observedAt);
                }

                observations.Add(new ArrInventoryRecordObservation(candidate, fileObservation));
            }
        }
        catch (ArgumentException)
        {
            return InventoryPopulation.NotStored(movies);
        }

        return cache.TryStore(connection, observedAt, observations)
            ? InventoryPopulation.Stored(movies, observations)
            : InventoryPopulation.NotStored(movies);
    }

    /// <summary>
    /// The direct provider read, unchanged: match the current library read and
    /// read the matched movie's file through the per-record endpoint. It is used
    /// when the inventory cache is unavailable or cannot retain the observation
    /// set.
    /// </summary>
    private async Task<ArrMetadataReadResult> ReadDirectAsync(
        MediaIdentity identity,
        ArrConnection connection,
        IReadOnlyList<RadarrMovieResource> movies,
        IRadarrReadClient client,
        CancellationToken cancellationToken)
    {
        List<MatchCandidate> candidates;
        try
        {
            candidates = new List<MatchCandidate>(movies.Count);
            foreach (var movie in movies)
            {
                candidates.Add(RadarrMatchCandidateFactory.FromMovie(connection, movie));
            }
        }
        catch (ArgumentException)
        {
            // A malformed provider resource is a bounded invalid response and
            // never becomes a guessed match.
            return ArrMetadataReadResult.Failure(InvalidResponse());
        }

        var match = MediaMatcher.Match(identity, connection.Provider, connection.ConnectionId, candidates);
        LogMatch(identity, connection, match);
        if (match.Status != MediaMatchStatus.Matched || match.RecordIdentity is not RadarrIdentity record)
        {
            return ArrMetadataReadResult.Success(match);
        }

        RadarrMovieResource? matchedMovie = null;
        foreach (var movie in movies)
        {
            if (movie.Id == record.MovieId)
            {
                matchedMovie = movie;
                break;
            }
        }

        if (matchedMovie is null)
        {
            // The matched record disappeared between the read and the mapping.
            // The bounded outcome is a matched state without metadata rather than
            // a guessed snapshot.
            return ArrMetadataReadResult.Success(match);
        }

        RadarrMovieFileResource? movieFile = null;
        if (record.MovieFileIdentity.Presence == ArrFilePresence.Present)
        {
            var filesResult = await client.GetMovieFilesAsync(record.MovieId, cancellationToken).ConfigureAwait(false);
            if (!filesResult.IsSuccess || filesResult.Value is null)
            {
                return ArrMetadataReadResult.Failure(filesResult.Error!);
            }

            var currentFileId = record.MovieFileIdentity.FileId;
            foreach (var file in filesResult.Value!)
            {
                if (file.Id == currentFileId)
                {
                    movieFile = file;
                    break;
                }
            }
        }

        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            var metadata = RadarrMetadataMapper.Map(connection, matchedMovie, movieFile, observedAt);
            return ArrMetadataReadResult.Success(match, metadata);
        }
        catch (ArgumentException)
        {
            return ArrMetadataReadResult.Failure(InvalidResponse());
        }
    }

    private static ArrProviderError InvalidResponse()
    {
        return new ArrProviderError(
            ArrProviderErrorCode.InvalidResponse,
            ArrErrorRetryability.Never,
            "The provider returned a resource that could not be mapped.");
    }

    /// <summary>
    /// Writes one bounded, secret-free matching-boundary record. Only the
    /// Jellyfin item identifier, the provider kind, the bounded match status and
    /// method, and the bounded ambiguity reason are emitted; the provider DTO,
    /// the API key, and the request are never available here (ADR-020 clause 4).
    /// </summary>
    private void LogMatch(MediaIdentity identity, ArrConnection connection, MediaMatch match)
    {
        if (_log is null || !_log.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var reason = match.AmbiguityReason is { } ambiguity ? " Reason: " + ambiguity : string.Empty;
        _log.Write(
            LogLevel.Debug,
            ArrTagsLogEvent.MatchResolved,
            FormattableString.Invariant(
                $"Match for Jellyfin item {identity.JellyfinItemId:D} via {connection.Provider.Kind.ToApiName()} resolved to {match.Status} ({match.MatchMethod}).{reason}"));
    }

    /// <summary>
    /// The bounded outcome of one inventory population: a library failure, a set
    /// that was not stored (over-bound or failed bulk read) with the library read
    /// retained for the direct fallback, or a stored canonical observation set.
    /// </summary>
    private sealed class InventoryPopulation
    {
        private InventoryPopulation(
            IReadOnlyList<RadarrMovieResource>? movies,
            ArrProviderError? error,
            IReadOnlyList<ArrInventoryRecordObservation>? observations)
        {
            Movies = movies;
            Error = error;
            Observations = observations;
        }

        public IReadOnlyList<RadarrMovieResource>? Movies { get; }

        public ArrProviderError? Error { get; }

        public IReadOnlyList<ArrInventoryRecordObservation>? Observations { get; }

        public static InventoryPopulation Failed(ArrProviderError error)
        {
            return new InventoryPopulation(null, error, null);
        }

        public static InventoryPopulation NotStored(IReadOnlyList<RadarrMovieResource> movies)
        {
            return new InventoryPopulation(movies, null, null);
        }

        public static InventoryPopulation Stored(
            IReadOnlyList<RadarrMovieResource> movies,
            IReadOnlyList<ArrInventoryRecordObservation> observations)
        {
            return new InventoryPopulation(movies, null, observations);
        }
    }
}
