namespace ArrTags.Configuration;

/// <summary>
/// Persisted configuration for one Sonarr or Radarr connection. Connections are
/// disabled by default so a fresh installation performs no external I/O.
/// </summary>
public sealed class ArrConnectionConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the connection is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the absolute base URL without an API path.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API key. The value is never surfaced in diagnostics.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the finite request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = OperationalLimits.DefaultRequestTimeoutSeconds;

    /// <summary>
    /// Gets or sets a value indicating whether certificate validation is relaxed
    /// for this connection only.
    /// </summary>
    public bool AllowInsecureTls { get; set; }
}
