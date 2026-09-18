using System;
using System.Collections.Generic;
using System.Globalization;
using ArrTags.Matching;

namespace ArrTags.Providers.Radarr;

/// <summary>
/// Translates a validated Radarr movie resource into the canonical,
/// connection-scoped <see cref="MatchCandidate"/>. It is the provider-side
/// candidate-assembly boundary: the Radarr DTO is consumed here and never
/// reaches the matching layer.
/// </summary>
public static class RadarrMatchCandidateFactory
{
    /// <summary>
    /// Builds the canonical match candidate for one Radarr movie.
    /// </summary>
    /// <param name="connection">The Radarr connection scope.</param>
    /// <param name="movie">The validated Radarr movie resource.</param>
    /// <returns>The connection-scoped match candidate.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection is not a Radarr connection or the movie identifier is invalid.</exception>
    public static MatchCandidate FromMovie(ArrConnection connection, RadarrMovieResource movie)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(movie);

        if (connection.Provider.Kind != ArrProviderKind.Radarr)
        {
            throw new ArgumentException("The connection is not a Radarr connection.", nameof(connection));
        }

        if (movie.Id <= 0)
        {
            throw new ArgumentException("A positive Radarr movie identifier is required.", nameof(movie));
        }

        var identity = RadarrMetadataMapper.MapIdentity(connection, movie);
        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddNumericProviderId(providerIds, MatchProviderIdKeys.Tmdb, movie.TmdbId);
        AddTextProviderId(providerIds, MatchProviderIdKeys.Imdb, movie.ImdbId);

        return new MatchCandidate(
            connection.ConnectionId,
            ArrProviderKind.Radarr,
            identity,
            providerIds,
            movie.Title,
            movie.Year);
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
