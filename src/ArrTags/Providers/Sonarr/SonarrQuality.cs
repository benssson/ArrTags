namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr quality descriptor. It is the
/// file's actual observed quality, never the series quality profile.
/// </summary>
public sealed class SonarrQuality
{
    /// <summary>
    /// Gets the provider-local quality identifier. It is stable only within one
    /// Sonarr instance and one release.
    /// </summary>
    public int? Id { get; init; }

    /// <summary>
    /// Gets the human-facing quality label.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the quality source, such as web, webRip, bluray, or television.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the numeric resolution when reported.
    /// </summary>
    public int? Resolution { get; init; }
}
