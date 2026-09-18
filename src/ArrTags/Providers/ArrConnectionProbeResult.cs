using System;

namespace ArrTags.Providers;

/// <summary>
/// The bounded outcome of probing one Arr connection. A successful probe carries
/// the observed provider identity; a failure carries a redacted error and never
/// exposes credentials.
/// </summary>
public sealed class ArrConnectionProbeResult
{
    private ArrConnectionProbeResult(ArrConnectionHealth health, ArrProvider? provider, ArrProviderError? error)
    {
        Health = health;
        Provider = provider;
        Error = error;
    }

    /// <summary>
    /// Gets the observed connection health.
    /// </summary>
    public ArrConnectionHealth Health { get; }

    /// <summary>
    /// Gets the observed provider identity when the probe succeeded.
    /// </summary>
    public ArrProvider? Provider { get; }

    /// <summary>
    /// Gets the redacted failure when the probe did not succeed.
    /// </summary>
    public ArrProviderError? Error { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is healthy.
    /// </summary>
    public bool IsHealthy => Health == ArrConnectionHealth.Healthy;

    /// <summary>
    /// Creates a healthy probe result.
    /// </summary>
    /// <param name="provider">The observed provider identity.</param>
    /// <returns>A healthy result.</returns>
    /// <exception cref="ArgumentNullException">The provider is <see langword="null"/>.</exception>
    public static ArrConnectionProbeResult Healthy(ArrProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new ArrConnectionProbeResult(ArrConnectionHealth.Healthy, provider, null);
    }

    /// <summary>
    /// Creates a failed probe result.
    /// </summary>
    /// <param name="health">The unhealthy connection state.</param>
    /// <param name="error">The redacted failure.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentException">The health is <see cref="ArrConnectionHealth.Healthy"/>.</exception>
    /// <exception cref="ArgumentNullException">The error is <see langword="null"/>.</exception>
    public static ArrConnectionProbeResult Failed(ArrConnectionHealth health, ArrProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (health == ArrConnectionHealth.Healthy)
        {
            throw new ArgumentException("A failed probe result cannot report healthy.", nameof(health));
        }

        return new ArrConnectionProbeResult(health, null, error);
    }
}
