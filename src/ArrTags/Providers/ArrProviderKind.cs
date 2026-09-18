namespace ArrTags.Providers;

/// <summary>
/// The supported Arr provider families. ArrTags reads both through the same
/// read-only v3 contract but keeps their local record identities scoped apart so
/// the same numeric ID on two providers cannot be confused.
/// </summary>
public enum ArrProviderKind
{
    /// <summary>
    /// Sonarr, the television series and episode manager.
    /// </summary>
    Sonarr,

    /// <summary>
    /// Radarr, the movie manager.
    /// </summary>
    Radarr,
}
