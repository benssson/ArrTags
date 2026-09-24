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

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The reconciliation reader for Sonarr. It composes the read-only Sonarr client,
/// the canonical series and episode candidate assembly, the documented
/// series-then-episode matching order, the validated episode-file join, and the
/// canonical metadata mapping. The Sonarr DTOs consumed here never leave this
/// provider layer; the returned match and metadata are provider-neutral.
/// </summary>
/// <remarks>
/// When the bounded provider inventory cache (ADR-018) is available, one
/// <c>api/v3/series</c> library read per connection is reused by every work item
/// in the cache window, and the episode file resources are resolved from the
/// per-series episode read (which embeds the current file) with one repeatable
/// <c>episodeFile?episodeFileIds=</c> bulk read for the remaining files. A cache
/// hit serves the canonical observations without calling the provider; concurrent
/// cold readers for the same connection serialize through the cache provider's
/// per-connection single-flight gate so one library read serves all of them. An
/// observation set that exceeds the configured record or byte bound, or an
/// episode or bulk file read that fails, keeps the existing direct read
/// unchanged. The cache is never authoritative: a missing or expired set is
/// re-read from the provider, and no provider conditional request or revision
/// token is used.
/// </remarks>
public sealed class SonarrMetadataReader : IArrMetadataReader
{
    private readonly IArrReadClientFactory _clients;
    private readonly IArrTagsLog<SonarrMetadataReader>? _log;
    private readonly ArrInventoryCacheProvider? _inventory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonarrMetadataReader"/> class.
    /// </summary>
    /// <param name="clients">The connection-scoped read client factory.</param>
    /// <param name="log">The optional bounded, secret-free matching-boundary log.</param>
    /// <param name="inventory">The optional bounded provider inventory cache (ADR-018); when absent the reader keeps the direct provider read.</param>
    /// <exception cref="ArgumentNullException">The factory is <see langword="null"/>.</exception>
    public SonarrMetadataReader(
        IArrReadClientFactory clients,
        IArrTagsLog<SonarrMetadataReader>? log = null,
        ArrInventoryCacheProvider? inventory = null)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _log = log;
        _inventory = inventory;
    }

    /// <inheritdoc />
    public ArrProviderKind Kind => ArrProviderKind.Sonarr;

    /// <inheritdoc />
    public async Task<ArrMetadataReadResult> ReadAsync(
        MediaIdentity identity,
        ArrConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        if (identity.SeriesIdentity is not MediaIdentity)
        {
            // The canonical matcher produces the bounded missing-series-context
            // outcome for an episode without parent context, and unsupported for
            // any non-episode item.
            var unsupported = MediaMatcher.MatchEpisode(
                identity,
                connection.Provider,
                connection.ConnectionId,
                Array.Empty<MatchCandidate>(),
                Array.Empty<MatchCandidate>());
            LogMatch(identity, connection, unsupported);
            return ArrMetadataReadResult.Success(unsupported);
        }

        var client = _clients.CreateSonarr(connection);
        var cache = _inventory?.Current;

        if (cache is null)
        {
            var libraryResult = await client.GetSeriesAsync(cancellationToken).ConfigureAwait(false);
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

        // The set is over the configured bound or a read failed: keep the
        // existing direct read unchanged, reusing the library read already made.
        return await ReadDirectAsync(identity, connection, population.Series!, client, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Matches and maps one item from a canonical observation set without any
    /// provider call. The series and episode candidates are matched through the
    /// documented series-then-episode order, and the matched episode's file
    /// observation is the cached canonical metadata.
    /// </summary>
    private ArrMetadataReadResult ReadFromObservations(
        MediaIdentity identity,
        ArrConnection connection,
        IReadOnlyList<ArrInventoryRecordObservation> observations)
    {
        var seriesCandidates = new List<MatchCandidate>();
        var episodeCandidates = new List<MatchCandidate>();
        foreach (var observation in observations)
        {
            if (observation.Candidate.RecordIdentity is not SonarrIdentity record)
            {
                continue;
            }

            if (record.EpisodeId is null)
            {
                seriesCandidates.Add(observation.Candidate);
            }
            else
            {
                episodeCandidates.Add(observation.Candidate);
            }
        }

        var match = MediaMatcher.MatchEpisode(
            identity,
            connection.Provider,
            connection.ConnectionId,
            seriesCandidates,
            episodeCandidates);
        LogMatch(identity, connection, match);
        if (match.Status != MediaMatchStatus.Matched || match.RecordIdentity is not SonarrIdentity episodeRecord)
        {
            return ArrMetadataReadResult.Success(match);
        }

        foreach (var observation in observations)
        {
            if (observation.Candidate.RecordIdentity is SonarrIdentity candidateRecord
                && candidateRecord.Equals(episodeRecord))
            {
                // An episode without a current file carries no cached file
                // observation. The direct read maps an explicit absent-file
                // observation with unknown values, so the cached path preserves
                // the same matched-with-metadata outcome.
                var metadata = observation.FileObservation
                    ?? new BadgeMetadata(connection.Provider, episodeRecord, DateTimeOffset.UtcNow);
                return ArrMetadataReadResult.Success(match, metadata);
            }
        }

        // The matched episode is not present in the observation set. The bounded
        // outcome is a matched state without metadata rather than a guessed
        // snapshot.
        return ArrMetadataReadResult.Success(match);
    }

    /// <summary>
    /// Reads the series library once, reads the episodes for every series, and
    /// resolves the episode files from the embedded episode resource plus one
    /// bounded bulk <c>episodeFile?episodeFileIds=</c> read (chunked by the
    /// client when the id list is large). It reports a library failure, an
    /// over-bound/failed-read fallback, or a stored set so the caller can serve
    /// the observations or keep the existing direct read unchanged.
    /// </summary>
    private async Task<InventoryPopulation> PopulateAsync(
        ArrConnection connection,
        ISonarrReadClient client,
        ArrInventoryCache cache,
        CancellationToken cancellationToken)
    {
        var libraryResult = await client.GetSeriesAsync(cancellationToken).ConfigureAwait(false);
        if (!libraryResult.IsSuccess || libraryResult.Value is null)
        {
            return InventoryPopulation.Failed(libraryResult.Error!);
        }

        IReadOnlyList<SonarrSeriesResource> series = libraryResult.Value!;
        var observedAt = DateTimeOffset.UtcNow;
        var observations = new List<ArrInventoryRecordObservation>();
        var episodesBySeries = new List<(SonarrSeriesResource Series, IReadOnlyList<SonarrEpisodeResource> Episodes)>(series.Count);
        var gapFileIds = new List<int>();
        var seenGapFileIds = new HashSet<int>();

        try
        {
            foreach (var candidateSeries in series)
            {
                observations.Add(new ArrInventoryRecordObservation(
                    SonarrMatchCandidateFactory.FromSeries(connection, candidateSeries)));

                var episodesResult = await client.GetEpisodesAsync(candidateSeries.Id, cancellationToken).ConfigureAwait(false);
                if (!episodesResult.IsSuccess || episodesResult.Value is null)
                {
                    // The episode read is part of the inventory; a bounded
                    // failure keeps the direct read instead of failing the work.
                    return InventoryPopulation.NotStored(series);
                }

                var episodes = episodesResult.Value!;
                episodesBySeries.Add((candidateSeries, episodes));
                foreach (var episode in episodes)
                {
                    if (episode is null || episode.SeriesId != candidateSeries.Id)
                    {
                        continue;
                    }

                    var identity = SonarrMetadataMapper.MapEpisodeIdentity(connection, candidateSeries, episode);
                    if (identity.EpisodeFileIdentity is { Presence: ArrFilePresence.Present, FileId: int fileId }
                        && SonarrEpisodeFileResolver.Resolve(episode, Array.Empty<SonarrEpisodeFileResource>()) is null
                        && seenGapFileIds.Add(fileId))
                    {
                        gapFileIds.Add(fileId);
                    }
                }
            }
        }
        catch (ArgumentException)
        {
            return InventoryPopulation.NotStored(series);
        }

        var filesById = new Dictionary<int, SonarrEpisodeFileResource>();
        if (gapFileIds.Count > 0)
        {
            var filesResult = await client.GetEpisodeFilesAsync(gapFileIds, cancellationToken).ConfigureAwait(false);
            if (!filesResult.IsSuccess || filesResult.Value is null)
            {
                return InventoryPopulation.NotStored(series);
            }

            foreach (var file in filesResult.Value!)
            {
                if (file is not null)
                {
                    filesById[file.Id] = file;
                }
            }
        }

        try
        {
            foreach (var (candidateSeries, episodes) in episodesBySeries)
            {
                foreach (var episode in episodes)
                {
                    if (episode is null || episode.SeriesId != candidateSeries.Id)
                    {
                        continue;
                    }

                    var candidate = SonarrMatchCandidateFactory.FromEpisode(connection, candidateSeries, episode);
                    BadgeMetadata? fileObservation = null;
                    if (candidate.RecordIdentity is SonarrIdentity identity
                        && identity.EpisodeFileIdentity is { Presence: ArrFilePresence.Present, FileId: int fileId })
                    {
                        var file = SonarrEpisodeFileResolver.Resolve(episode, Array.Empty<SonarrEpisodeFileResource>());
                        if (file is null)
                        {
                            filesById.TryGetValue(fileId, out file);
                        }

                        fileObservation = SonarrMetadataMapper.Map(connection, candidateSeries, episode, file, observedAt);
                    }

                    observations.Add(new ArrInventoryRecordObservation(candidate, fileObservation));
                }
            }
        }
        catch (ArgumentException)
        {
            return InventoryPopulation.NotStored(series);
        }

        return cache.TryStore(connection, observedAt, observations)
            ? InventoryPopulation.Stored(series, observations)
            : InventoryPopulation.NotStored(series);
    }

    /// <summary>
    /// The direct provider read, unchanged: match the current series library read,
    /// read the matched series' episodes, and resolve the matched episode's file
    /// through the validated join. It is used when the inventory cache is
    /// unavailable or cannot retain the observation set.
    /// </summary>
    private async Task<ArrMetadataReadResult> ReadDirectAsync(
        MediaIdentity identity,
        ArrConnection connection,
        IReadOnlyList<SonarrSeriesResource> series,
        ISonarrReadClient client,
        CancellationToken cancellationToken)
    {
        List<MatchCandidate> seriesCandidates;
        try
        {
            seriesCandidates = new List<MatchCandidate>(series.Count);
            foreach (var candidate in series)
            {
                seriesCandidates.Add(SonarrMatchCandidateFactory.FromSeries(connection, candidate));
            }
        }
        catch (ArgumentException)
        {
            return ArrMetadataReadResult.Failure(InvalidResponse());
        }

        var seriesMatch = MediaMatcher.Match(identity.SeriesIdentity!, connection.Provider, connection.ConnectionId, seriesCandidates);
        if (seriesMatch.Status != MediaMatchStatus.Matched || seriesMatch.RecordIdentity is not SonarrIdentity seriesRecord)
        {
            // The parent series did not match, so the episode is not evaluated.
            // The canonical matcher supplies the bounded outcome and reason.
            var unmatched = MediaMatcher.MatchEpisode(
                identity,
                connection.Provider,
                connection.ConnectionId,
                seriesCandidates,
                Array.Empty<MatchCandidate>());
            LogMatch(identity, connection, unmatched);
            return ArrMetadataReadResult.Success(unmatched);
        }

        SonarrSeriesResource? matchedSeries = null;
        foreach (var candidate in series)
        {
            if (candidate.Id == seriesRecord.SeriesId)
            {
                matchedSeries = candidate;
                break;
            }
        }

        if (matchedSeries is null)
        {
            var unmatched = MediaMatcher.MatchEpisode(
                identity,
                connection.Provider,
                connection.ConnectionId,
                seriesCandidates,
                Array.Empty<MatchCandidate>());
            LogMatch(identity, connection, unmatched);
            return ArrMetadataReadResult.Success(unmatched);
        }

        var episodesResult = await client.GetEpisodesAsync(seriesRecord.SeriesId, cancellationToken).ConfigureAwait(false);
        if (!episodesResult.IsSuccess || episodesResult.Value is null)
        {
            return ArrMetadataReadResult.Failure(episodesResult.Error!);
        }

        IReadOnlyList<SonarrEpisodeResource> episodes = episodesResult.Value!;

        List<MatchCandidate> episodeCandidates;
        try
        {
            episodeCandidates = new List<MatchCandidate>(episodes.Count);
            foreach (var episode in episodes)
            {
                if (episode.SeriesId == seriesRecord.SeriesId)
                {
                    episodeCandidates.Add(SonarrMatchCandidateFactory.FromEpisode(connection, matchedSeries, episode));
                }
            }
        }
        catch (ArgumentException)
        {
            return ArrMetadataReadResult.Failure(InvalidResponse());
        }

        var observedAt = DateTimeOffset.UtcNow;
        var match = MediaMatcher.MatchEpisode(
            identity,
            connection.Provider,
            connection.ConnectionId,
            seriesCandidates,
            episodeCandidates);
        LogMatch(identity, connection, match);

        if (match.Status != MediaMatchStatus.Matched || match.RecordIdentity is not SonarrIdentity episodeRecord)
        {
            return ArrMetadataReadResult.Success(match);
        }

        SonarrEpisodeResource? matchedEpisode = null;
        foreach (var episode in episodes)
        {
            if (episode.Id == episodeRecord.EpisodeId)
            {
                matchedEpisode = episode;
                break;
            }
        }

        if (matchedEpisode is null)
        {
            return ArrMetadataReadResult.Success(match);
        }

        var episodeFile = SonarrEpisodeFileResolver.Resolve(matchedEpisode, Array.Empty<SonarrEpisodeFileResource>());
        if (episodeFile is null
            && matchedEpisode.EpisodeFileId is int episodeFileId
            && episodeFileId > 0)
        {
            var filesResult = await client.GetEpisodeFilesAsync(seriesRecord.SeriesId, cancellationToken).ConfigureAwait(false);
            if (!filesResult.IsSuccess || filesResult.Value is null)
            {
                return ArrMetadataReadResult.Failure(filesResult.Error!);
            }

            episodeFile = SonarrEpisodeFileResolver.Resolve(matchedEpisode, filesResult.Value!);
        }

        try
        {
            var metadata = SonarrMetadataMapper.Map(connection, matchedSeries, matchedEpisode, episodeFile, observedAt);
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
    /// that was not stored (over-bound or failed read) with the library read
    /// retained for the direct fallback, or a stored canonical observation set.
    /// </summary>
    private sealed class InventoryPopulation
    {
        private InventoryPopulation(
            IReadOnlyList<SonarrSeriesResource>? series,
            ArrProviderError? error,
            IReadOnlyList<ArrInventoryRecordObservation>? observations)
        {
            Series = series;
            Error = error;
            Observations = observations;
        }

        public IReadOnlyList<SonarrSeriesResource>? Series { get; }

        public ArrProviderError? Error { get; }

        public IReadOnlyList<ArrInventoryRecordObservation>? Observations { get; }

        public static InventoryPopulation Failed(ArrProviderError error)
        {
            return new InventoryPopulation(null, error, null);
        }

        public static InventoryPopulation NotStored(IReadOnlyList<SonarrSeriesResource> series)
        {
            return new InventoryPopulation(series, null, null);
        }

        public static InventoryPopulation Stored(
            IReadOnlyList<SonarrSeriesResource> series,
            IReadOnlyList<ArrInventoryRecordObservation> observations)
        {
            return new InventoryPopulation(series, null, observations);
        }
    }
}
