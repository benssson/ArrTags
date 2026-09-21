using System;
using ArrTags.Configuration;
using ArrTags.Secrets;

namespace ArrTags.Webhooks;

/// <summary>
/// The bounded outcome of authenticating an inbound webhook request. Every
/// non-<see cref="Authenticated"/> value fails closed; the controller maps them
/// all to the same safe status code so the response never reveals whether a
/// secret is configured or why a candidate was rejected.
/// </summary>
public enum WebhookAuthenticationResult
{
    /// <summary>The candidate matched the configured shared secret.</summary>
    Authenticated,

    /// <summary>No webhook secret is configured, or no lease could be acquired.</summary>
    NotConfigured,

    /// <summary>The request did not present a usable secret candidate.</summary>
    MissingCredential,

    /// <summary>The presented candidate did not match the configured secret.</summary>
    Mismatch,
}

/// <summary>
/// Authenticates an inbound webhook request against the configured shared secret
/// through the ADR-005 versioned secret boundary. The candidate is compared with
/// <see cref="SecretLease.Matches(string?)"/>, which uses a constant-time
/// comparison, and the secret value is never logged, returned, or stored. The
/// comparison is bounded: an oversized candidate is rejected before any
/// allocation proportional to it (ADR-012).
/// </summary>
public static class WebhookAuthentication
{
    /// <summary>
    /// The maximum accepted secret candidate length. A longer candidate is a
    /// bounded mismatch rather than an allocation.
    /// </summary>
    public const int MaxCandidateLength = 1024;

    /// <summary>
    /// Authenticates a candidate against the webhook secret slot for one
    /// configuration generation.
    /// </summary>
    /// <param name="resolver">The versioned secret resolver.</param>
    /// <param name="snapshot">The configuration generation the request observed.</param>
    /// <param name="candidate">The untrusted candidate from the request header.</param>
    /// <returns>The bounded authentication outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static WebhookAuthenticationResult Authenticate(
        IPluginSecretResolver resolver,
        PluginConfigurationSnapshot snapshot,
        string? candidate)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.WebhookConfigured)
        {
            return WebhookAuthenticationResult.NotConfigured;
        }

        if (string.IsNullOrEmpty(candidate))
        {
            return WebhookAuthenticationResult.MissingCredential;
        }

        if (candidate.Length > MaxCandidateLength)
        {
            return WebhookAuthenticationResult.Mismatch;
        }

        if (!resolver.TryAcquire(
                SecretReference.WebhookAuthentication,
                snapshot.ConfigurationVersion,
                out var lease)
            || lease is null)
        {
            // The configuration generation changed, the slot is unavailable, or
            // no secret is configured. Fail closed without touching the secret.
            return WebhookAuthenticationResult.NotConfigured;
        }

        using (lease)
        {
            return lease.Matches(candidate)
                ? WebhookAuthenticationResult.Authenticated
                : WebhookAuthenticationResult.Mismatch;
        }
    }
}
