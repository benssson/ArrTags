namespace ArrTags.Providers.Radarr;

/// <summary>
/// The integration-boundary DTO for a Radarr quality descriptor.
/// </summary>
public sealed class RadarrQuality
{
    /// <summary>
    /// Gets the provider-local quality identifier. It is stable only within one
    /// Radarr instance and one release.
    /// </summary>
    public int? Id { get; init; }

    /// <summary>
    /// Gets the human-facing quality label.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the quality source, such as webdl, webrip, or bluray.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the numeric resolution when reported.
    /// </summary>
    public int? Resolution { get; init; }

    /// <summary>
    /// Gets the quality modifier, such as none or remux.
    /// </summary>
    public string? Modifier { get; init; }
}
