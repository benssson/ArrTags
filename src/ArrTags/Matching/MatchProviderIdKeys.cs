namespace ArrTags.Matching;

/// <summary>
/// The normalized external provider identifier keys shared by the documented
/// matching order and provider-to-candidate mapping. Keys are compared
/// case-insensitively by <see cref="ProviderIdMatchRule"/> and
/// <see cref="MatchCandidate"/>.
/// </summary>
public static class MatchProviderIdKeys
{
    /// <summary>
    /// The TVDB identifier key used for Sonarr series and episodes.
    /// </summary>
    public const string Tvdb = "Tvdb";

    /// <summary>
    /// The TMDb identifier key used for Radarr movies and Sonarr series.
    /// </summary>
    public const string Tmdb = "Tmdb";

    /// <summary>
    /// The IMDb identifier key used for Radarr movies and Sonarr series.
    /// </summary>
    public const string Imdb = "Imdb";
}
