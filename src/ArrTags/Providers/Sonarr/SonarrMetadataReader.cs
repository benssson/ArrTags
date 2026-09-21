using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Reconciliation;

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The reconciliation reader for Sonarr. It composes the read-only Sonarr client,
/// the canonical series and episode candidate assembly, the documented
/// series-then-episode matching order, the validated episode-file join, and the
/// canonical metadata mapping. The Sonarr DTOs consumed here never leave this
/// provider layer; the returned match and metadata are provider-neutral.
/// </summary>
public sealed class SonarrMetadataReader : IArrMetadataReader
{
    private readonly IArrReadClientFactory _clients;

    /// <summary>
    /// Initializes a new instance of the <see cref="SonarrMetadataReader"/> class.
    /// </summary>
    /// <param name="clients">The connection-scoped read client factory.</param>
    /// <exception cref="ArgumentNullException">The factory is <see langword="null"/>.</exception>
    public SonarrMetadataReader(IArrReadClientFactory clients)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
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

        if (identity.SeriesIdentity is not MediaIdentity seriesIdentity)
        {
            // The canonical matcher produces the bounded missing-series-context
            // outcome for an episode without parent context, and unsupported for
            // any non-episode item.
            return ArrMetadataReadResult.Success(MediaMatcher.MatchEpisode(
                identity,
                connection.Provider,
                connection.ConnectionId,
                Array.Empty<MatchCandidate>(),
                Array.Empty<MatchCandidate>()));
        }

        var client = _clients.CreateSonarr(connection);
        var seriesResult = await client.GetSeriesAsync(cancellationToken).ConfigureAwait(false);
        if (!seriesResult.IsSuccess || seriesResult.Value is null)
        {
            return ArrMetadataReadResult.Failure(seriesResult.Error!);
        }

        IReadOnlyList<SonarrSeriesResource> series = seriesResult.Value!;

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

        var seriesMatch = MediaMatcher.Match(seriesIdentity, connection.Provider, connection.ConnectionId, seriesCandidates);
        if (seriesMatch.Status != MediaMatchStatus.Matched || seriesMatch.RecordIdentity is not SonarrIdentity seriesRecord)
        {
            // The parent series did not match, so the episode is not evaluated.
            // The canonical matcher supplies the bounded outcome and reason.
            return ArrMetadataReadResult.Success(MediaMatcher.MatchEpisode(
                identity,
                connection.Provider,
                connection.ConnectionId,
                seriesCandidates,
                Array.Empty<MatchCandidate>()));
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
            return ArrMetadataReadResult.Success(MediaMatcher.MatchEpisode(
                identity,
                connection.Provider,
                connection.ConnectionId,
                seriesCandidates,
                Array.Empty<MatchCandidate>()));
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
}
