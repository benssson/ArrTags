namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a matched Sonarr custom format.
/// </summary>
public sealed class SonarrCustomFormatResource
{
    /// <summary>
    /// Gets the provider-local custom-format identifier.
    /// </summary>
    public int? Id { get; init; }

    /// <summary>
    /// Gets the custom-format name.
    /// </summary>
    public string? Name { get; init; }
}
