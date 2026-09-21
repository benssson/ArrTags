using System;
using ArrTags.Providers;

namespace ArrTags.Reconciliation;

/// <summary>
/// A provider-neutral, secret-free, serializable snapshot of a typed
/// <see cref="ArrRecordIdentity"/>. The canonical identity is an abstract,
/// connection-scoped type carrying no JSON discriminator; this snapshot flattens
/// the concrete Sonarr and Radarr components into nullable fields so the
/// metadata cache record round-trips without provider DTOs.
/// </summary>
public sealed class MetadataRecordIdentity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataRecordIdentity"/> class.
    /// </summary>
    /// <param name="providerKind">The provider family that selects the identity shape.</param>
    /// <param name="connectionId">The stable, non-secret connection scope.</param>
    /// <param name="movieId">The Radarr-local movie identifier for a Radarr identity.</param>
    /// <param name="movieFilePresence">The explicit Radarr movie-file presence.</param>
    /// <param name="movieFileId">The Radarr-local movie-file identifier when present.</param>
    /// <param name="seriesId">The Sonarr-local series identifier for a Sonarr identity.</param>
    /// <param name="episodeId">The Sonarr-local episode identifier for an episode identity.</param>
    /// <param name="episodeFilePresence">The explicit Sonarr episode-file presence.</param>
    /// <param name="episodeFileId">The Sonarr-local episode-file identifier when present.</param>
    public MetadataRecordIdentity(
        ArrProviderKind providerKind,
        string connectionId,
        int? movieId = null,
        ArrFilePresence? movieFilePresence = null,
        int? movieFileId = null,
        int? seriesId = null,
        int? episodeId = null,
        ArrFilePresence? episodeFilePresence = null,
        int? episodeFileId = null)
    {
        ProviderKind = providerKind;
        ConnectionId = connectionId ?? string.Empty;
        MovieId = movieId;
        MovieFilePresence = movieFilePresence;
        MovieFileId = movieFileId;
        SeriesId = seriesId;
        EpisodeId = episodeId;
        EpisodeFilePresence = episodeFilePresence;
        EpisodeFileId = episodeFileId;
    }

    /// <summary>
    /// Gets the provider family that selects the identity shape.
    /// </summary>
    public ArrProviderKind ProviderKind { get; }

    /// <summary>
    /// Gets the stable, non-secret connection scope.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the Radarr-local movie identifier for a Radarr identity.
    /// </summary>
    public int? MovieId { get; }

    /// <summary>
    /// Gets the explicit Radarr movie-file presence for a Radarr identity.
    /// </summary>
    public ArrFilePresence? MovieFilePresence { get; }

    /// <summary>
    /// Gets the Radarr-local movie-file identifier when present.
    /// </summary>
    public int? MovieFileId { get; }

    /// <summary>
    /// Gets the Sonarr-local series identifier for a Sonarr identity.
    /// </summary>
    public int? SeriesId { get; }

    /// <summary>
    /// Gets the Sonarr-local episode identifier for an episode identity.
    /// </summary>
    public int? EpisodeId { get; }

    /// <summary>
    /// Gets the explicit Sonarr episode-file presence for an episode identity.
    /// </summary>
    public ArrFilePresence? EpisodeFilePresence { get; }

    /// <summary>
    /// Gets the Sonarr-local episode-file identifier when present.
    /// </summary>
    public int? EpisodeFileId { get; }

    /// <summary>
    /// Creates the serializable snapshot from a canonical record identity.
    /// </summary>
    /// <param name="identity">The canonical record identity.</param>
    /// <returns>The serializable snapshot.</returns>
    /// <exception cref="ArgumentNullException">The identity is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The identity has an unsupported concrete shape.</exception>
    public static MetadataRecordIdentity From(ArrRecordIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        switch (identity)
        {
            case RadarrIdentity radarr:
                return new MetadataRecordIdentity(
                    identity.ProviderKind,
                    identity.ConnectionId.Value,
                    movieId: radarr.MovieId,
                    movieFilePresence: radarr.MovieFileIdentity.Presence,
                    movieFileId: radarr.MovieFileIdentity.FileId);
            case SonarrIdentity sonarr:
                return new MetadataRecordIdentity(
                    identity.ProviderKind,
                    identity.ConnectionId.Value,
                    seriesId: sonarr.SeriesId,
                    episodeId: sonarr.EpisodeId,
                    episodeFilePresence: sonarr.EpisodeFileIdentity?.Presence,
                    episodeFileId: sonarr.EpisodeFileIdentity?.FileId);
            default:
                throw new ArgumentException(
                    "The record identity has an unsupported concrete shape.",
                    nameof(identity));
        }
    }

    /// <summary>
    /// Validates the snapshot field invariants for the provider-specific shape.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation when invalid.</param>
    /// <returns><see langword="true"/> when the snapshot is valid.</returns>
    public bool Validate(out string reason)
    {
        if (!Enum.IsDefined(ProviderKind))
        {
            reason = "The record identity provider kind is not defined.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ConnectionId))
        {
            reason = "The record identity requires a connection scope.";
            return false;
        }

        switch (ProviderKind)
        {
            case ArrProviderKind.Radarr:
                if (MovieId is not int movieId || movieId <= 0)
                {
                    reason = "A Radarr identity requires a positive movie identifier.";
                    return false;
                }

                if (MovieFilePresence is not ArrFilePresence)
                {
                    reason = "A Radarr identity requires an explicit movie-file presence.";
                    return false;
                }

                if (MovieFilePresence == ArrFilePresence.Present && (MovieFileId is not int movieFileId || movieFileId <= 0))
                {
                    reason = "A present Radarr movie file requires a positive identifier.";
                    return false;
                }

                if (MovieFilePresence == ArrFilePresence.Absent && MovieFileId is not null)
                {
                    reason = "An absent Radarr movie file cannot carry an identifier.";
                    return false;
                }

                break;
            case ArrProviderKind.Sonarr:
                if (SeriesId is not int seriesId || seriesId <= 0)
                {
                    reason = "A Sonarr identity requires a positive series identifier.";
                    return false;
                }

                if (EpisodeId is int episodeId && episodeId <= 0)
                {
                    reason = "A Sonarr episode identifier must be positive.";
                    return false;
                }

                if (EpisodeId is null && (EpisodeFilePresence is not null || EpisodeFileId is not null))
                {
                    reason = "A Sonarr series identity cannot carry an episode-file identity.";
                    return false;
                }

                if (EpisodeId is not null)
                {
                    if (EpisodeFilePresence is not ArrFilePresence)
                    {
                        reason = "A Sonarr episode identity requires an explicit episode-file presence.";
                        return false;
                    }

                    if (EpisodeFilePresence == ArrFilePresence.Present && (EpisodeFileId is not int episodeFileId || episodeFileId <= 0))
                    {
                        reason = "A present Sonarr episode file requires a positive identifier.";
                        return false;
                    }

                    if (EpisodeFilePresence == ArrFilePresence.Absent && EpisodeFileId is not null)
                    {
                        reason = "An absent Sonarr episode file cannot carry an identifier.";
                        return false;
                    }
                }

                break;
            default:
                reason = "The record identity provider kind is not supported.";
                return false;
        }

        reason = string.Empty;
        return true;
    }
}
