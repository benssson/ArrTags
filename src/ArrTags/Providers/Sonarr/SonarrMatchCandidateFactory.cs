using System;
using System.Collections.Generic;
using System.Globalization;
using ArrTags.Matching;

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// Translates validated Sonarr series and episode resources into the canonical,
/// connection-scoped <see cref="MatchCandidate"/>. It is the provider-side
/// candidate-assembly boundary: the Sonarr DTOs are consumed here and never
/// reach the matching layer.
/// </summary>
public static class SonarrMatchCandidateFactory
{
    /// <summary>
    /// Builds the canonical series match candidate for one Sonarr series.
    /// </summary>
    /// <param name="connection">The Sonarr connection scope.</param>
    /// <param name="series">The validated Sonarr series resource.</param>
    /// <returns>The connection-scoped series candidate.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection is not a Sonarr connection or the series identifier is invalid.</exception>
    public static MatchCandidate FromSeries(ArrConnection connection, SonarrSeriesResource series)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(series);

        EnsureConnection(connection);
        var identity = SonarrMetadataMapper.MapSeriesIdentity(connection, series);

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddNumericProviderId(providerIds, MatchProviderIdKeys.Tvdb, series.TvdbId);
        AddNumericProviderId(providerIds, MatchProviderIdKeys.Tmdb, series.TmdbId);
        AddTextProviderId(providerIds, MatchProviderIdKeys.Imdb, series.ImdbId);

        return new MatchCandidate(
            connection.ConnectionId,
            ArrProviderKind.Sonarr,
            identity,
            providerIds,
            series.Title,
            series.Year,
            primaryPath: series.Path);
    }

    /// <summary>
    /// Builds the canonical episode match candidate for one Sonarr episode. The
    /// candidate is always anchored to its series identity so the matching order
    /// can scope episode evaluation to the matched parent series.
    /// </summary>
    /// <param name="connection">The Sonarr connection scope.</param>
    /// <param name="series">The series the episode belongs to.</param>
    /// <param name="episode">The validated Sonarr episode resource.</param>
    /// <returns>The connection-scoped episode candidate.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection, series, or episode identifiers are inconsistent.</exception>
    public static MatchCandidate FromEpisode(
        ArrConnection connection,
        SonarrSeriesResource series,
        SonarrEpisodeResource episode)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episode);

        EnsureConnection(connection);
        var identity = SonarrMetadataMapper.MapEpisodeIdentity(connection, series, episode);

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddNumericProviderId(providerIds, MatchProviderIdKeys.Tvdb, episode.TvdbId);

        return new MatchCandidate(
            connection.ConnectionId,
            ArrProviderKind.Sonarr,
            identity,
            providerIds,
            episode.Title,
            seasonNumber: episode.SeasonNumber,
            episodeNumber: episode.EpisodeNumber);
    }

    private static void EnsureConnection(ArrConnection connection)
    {
        if (connection.Provider.Kind != ArrProviderKind.Sonarr)
        {
            throw new ArgumentException("The connection is not a Sonarr connection.", nameof(connection));
        }
    }

    private static void AddNumericProviderId(Dictionary<string, string> providerIds, string key, int? value)
    {
        if (value is int numeric && numeric > 0)
        {
            providerIds[key] = numeric.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void AddTextProviderId(Dictionary<string, string> providerIds, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            providerIds[key] = value.Trim();
        }
    }
}
