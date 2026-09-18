namespace ArrTags.Providers.Radarr;

/// <summary>
/// The integration-boundary DTO for a Radarr <c>SystemResource</c> response.
/// Unknown or absent fields remain <see langword="null"/>.
/// </summary>
public sealed class RadarrSystemStatusResource
{
    /// <summary>
    /// Gets the application name reported by the server.
    /// </summary>
    public string? AppName { get; init; }

    /// <summary>
    /// Gets the configured instance name.
    /// </summary>
    public string? InstanceName { get; init; }

    /// <summary>
    /// Gets the reported application version.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the configured URL base.
    /// </summary>
    public string? UrlBase { get; init; }

    /// <summary>
    /// Gets the configured authentication mode.
    /// </summary>
    public string? Authentication { get; init; }
}
