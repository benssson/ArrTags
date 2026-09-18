namespace ArrTags.Providers.Radarr;

/// <summary>
/// The integration-boundary DTO for a matched Radarr custom format.
/// </summary>
public sealed class RadarrCustomFormatResource
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
