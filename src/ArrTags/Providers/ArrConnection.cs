using System;
using ArrTags.Secrets;

namespace ArrTags.Providers;

/// <summary>
/// The canonical, immutable identity and configuration of one Sonarr or Radarr
/// connection. It carries only a "has API key" flag; the key value never leaves
/// the persisted configuration and is never stored in canonical state.
/// </summary>
public sealed class ArrConnection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrConnection"/> class.
    /// </summary>
    /// <param name="connectionId">The stable connection scope.</param>
    /// <param name="provider">The provider identity.</param>
    /// <param name="baseUrl">The configured base URL without an API path.</param>
    /// <param name="enabled">Whether the connection may be queried.</param>
    /// <param name="requestTimeoutSeconds">The finite request timeout in seconds.</param>
    /// <param name="tlsPolicy">The certificate validation policy.</param>
    /// <param name="apiKeyReference">The safe slot that resolves this connection's API key.</param>
    /// <param name="configurationVersion">The configuration generation this connection was derived from.</param>
    /// <param name="hasApiKey">Whether an API key is configured, never its value.</param>
    /// <param name="health">The cached connection health.</param>
    /// <param name="lastProbedAt">The last probe time when known.</param>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public ArrConnection(
        ArrConnectionId connectionId,
        ArrProvider provider,
        string baseUrl,
        bool enabled,
        int requestTimeoutSeconds,
        ArrTlsPolicy tlsPolicy,
        SecretReference apiKeyReference,
        long configurationVersion,
        bool hasApiKey,
        ArrConnectionHealth health,
        DateTimeOffset? lastProbedAt)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(apiKeyReference);

        ConnectionId = connectionId;
        Provider = provider;
        BaseUrl = baseUrl;
        Enabled = enabled;
        RequestTimeoutSeconds = requestTimeoutSeconds;
        TlsPolicy = tlsPolicy;
        ApiKeyReference = apiKeyReference;
        ConfigurationVersion = configurationVersion;
        HasApiKey = hasApiKey;
        Health = health;
        LastProbedAt = lastProbedAt;
    }

    /// <summary>
    /// Gets the stable connection scope used for provider records and cache keys.
    /// </summary>
    public ArrConnectionId ConnectionId { get; }

    /// <summary>
    /// Gets the provider identity.
    /// </summary>
    public ArrProvider Provider { get; }

    /// <summary>
    /// Gets the configured base URL without an API path.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Gets a value indicating whether the connection may be queried. Disabled
    /// connections are never contacted.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// Gets the finite request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; }

    /// <summary>
    /// Gets the certificate validation policy.
    /// </summary>
    public ArrTlsPolicy TlsPolicy { get; }

    /// <summary>
    /// Gets the safe slot reference used to acquire this connection's API key.
    /// It never contains the secret value.
    /// </summary>
    public SecretReference ApiKeyReference { get; }

    /// <summary>
    /// Gets the configuration generation this connection was derived from. A
    /// credential lease is valid only while this generation is current.
    /// </summary>
    public long ConfigurationVersion { get; }

    /// <summary>
    /// Gets a value indicating whether an API key is configured. The key value is
    /// never exposed.
    /// </summary>
    public bool HasApiKey { get; }

    /// <summary>
    /// Gets the cached connection health.
    /// </summary>
    public ArrConnectionHealth Health { get; }

    /// <summary>
    /// Gets the last probe time when known.
    /// </summary>
    public DateTimeOffset? LastProbedAt { get; }
}
