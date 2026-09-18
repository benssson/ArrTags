using System;
using System.Globalization;

namespace ArrTags.Providers;

/// <summary>
/// The connection-scoped Sonarr identity of a matched series or episode. A
/// series match carries the series identifier only; an episode match always
/// distinguishes the series, the episode, and the current episode file (which
/// may be explicitly absent).
/// </summary>
public sealed class SonarrIdentity : ArrRecordIdentity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SonarrIdentity"/> class for a
    /// matched series.
    /// </summary>
    /// <param name="connectionId">The connection scope.</param>
    /// <param name="seriesId">The Sonarr-local series identifier.</param>
    /// <exception cref="ArgumentNullException">The connection identifier is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The series identifier is not positive.</exception>
    public SonarrIdentity(ArrConnectionId connectionId, int seriesId)
        : this(connectionId, seriesId, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SonarrIdentity"/> class for a
    /// matched episode and its current file identity.
    /// </summary>
    /// <param name="connectionId">The connection scope.</param>
    /// <param name="seriesId">The Sonarr-local series identifier.</param>
    /// <param name="episodeId">The Sonarr-local episode identifier.</param>
    /// <param name="episodeFileIdentity">The current episode-file identity, including explicit absence.</param>
    /// <exception cref="ArgumentException">The episode-file identity is missing for an episode.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A local identifier is not positive.</exception>
    public SonarrIdentity(
        ArrConnectionId connectionId,
        int seriesId,
        int episodeId,
        ArrFileIdentity episodeFileIdentity)
        : this(connectionId, seriesId, (int?)episodeId, episodeFileIdentity)
    {
    }

    private SonarrIdentity(
        ArrConnectionId connectionId,
        int seriesId,
        int? episodeId,
        ArrFileIdentity? episodeFileIdentity)
        : base(connectionId, ArrProviderKind.Sonarr)
    {
        if (seriesId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seriesId), seriesId, "A Sonarr series identifier must be positive.");
        }

        if (episodeId is int episode && episode <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(episodeId), episodeId, "A Sonarr episode identifier must be positive.");
        }

        if (episodeId is null && episodeFileIdentity is not null)
        {
            throw new ArgumentException(
                "A series identity cannot carry an episode-file identity.",
                nameof(episodeFileIdentity));
        }

        if (episodeId is not null && episodeFileIdentity is null)
        {
            throw new ArgumentException(
                "A matched Sonarr episode requires an explicit episode-file identity.",
                nameof(episodeFileIdentity));
        }

        SeriesId = seriesId;
        EpisodeId = episodeId;
        EpisodeFileIdentity = episodeFileIdentity;
    }

    /// <summary>
    /// Gets the Sonarr-local series identifier. It is distinct from an episode
    /// TVDB identifier.
    /// </summary>
    public int SeriesId { get; }

    /// <summary>
    /// Gets the Sonarr-local episode identifier when this is an episode match.
    /// </summary>
    public int? EpisodeId { get; }

    /// <summary>
    /// Gets the current episode-file identity when this is an episode match.
    /// </summary>
    public ArrFileIdentity? EpisodeFileIdentity { get; }

    /// <inheritdoc />
    protected override bool EqualsCore(ArrRecordIdentity other)
    {
        var sonarr = (SonarrIdentity)other;
        return SeriesId == sonarr.SeriesId
            && EpisodeId == sonarr.EpisodeId
            && Equals(EpisodeFileIdentity, sonarr.EpisodeFileIdentity);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ConnectionId, ProviderKind, SeriesId, EpisodeId, EpisodeFileIdentity);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var series = SeriesId.ToString(CultureInfo.InvariantCulture);
        if (EpisodeId is not int episodeId)
        {
            return "sonarr|" + ConnectionId.Value + "|series:" + series;
        }

        var episode = episodeId.ToString(CultureInfo.InvariantCulture);
        return "sonarr|" + ConnectionId.Value + "|series:" + series + "|episode:" + episode + "|" + EpisodeFileIdentity;
    }
}
