namespace ArrTags.Providers.Sonarr;

/// <summary>
/// The integration-boundary DTO for a Sonarr <c>LanguageResource</c>.
/// </summary>
public sealed class SonarrLanguageResource
{
    /// <summary>
    /// Gets the provider-local language identifier.
    /// </summary>
    public int? Id { get; init; }

    /// <summary>
    /// Gets the language name.
    /// </summary>
    public string? Name { get; init; }
}
