using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Logging;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Reconciliation;
using Microsoft.Extensions.Logging;

namespace ArrTags.Providers.Radarr;

/// <summary>
/// The reconciliation reader for Radarr. It composes the read-only Radarr client,
/// the canonical movie-candidate assembly, the documented movie matching order,
/// and the canonical metadata mapping. The Radarr DTOs consumed here never leave
/// this provider layer; the returned match and metadata are provider-neutral.
/// </summary>
public sealed class RadarrMetadataReader : IArrMetadataReader
{
    private readonly IArrReadClientFactory _clients;
    private readonly IArrTagsLog<RadarrMetadataReader>? _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="RadarrMetadataReader"/> class.
    /// </summary>
    /// <param name="clients">The connection-scoped read client factory.</param>
    /// <param name="log">The optional bounded, secret-free matching-boundary log.</param>
    /// <exception cref="ArgumentNullException">The factory is <see langword="null"/>.</exception>
    public RadarrMetadataReader(
        IArrReadClientFactory clients,
        IArrTagsLog<RadarrMetadataReader>? log = null)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _log = log;
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
        var moviesResult = await client.GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        if (!moviesResult.IsSuccess || moviesResult.Value is null)
        {
            return ArrMetadataReadResult.Failure(moviesResult.Error!);
        }

        IReadOnlyList<RadarrMovieResource> movies = moviesResult.Value!;
        var observedAt = DateTimeOffset.UtcNow;

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
}
