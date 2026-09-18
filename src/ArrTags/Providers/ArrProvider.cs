using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace ArrTags.Providers;

/// <summary>
/// The canonical provider identity for one configured Arr server. It exposes the
/// provider family, instance scope, observed application version, API contract,
/// and derived capabilities without exposing provider DTOs or credentials.
/// </summary>
public sealed class ArrProvider
{
    /// <summary>
    /// The API contract used by both initial provider integrations.
    /// </summary>
    public const string V3ApiContract = "v3";

    private static readonly FrozenSet<string> NoCapabilities =
        Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrProvider"/> class.
    /// </summary>
    /// <param name="kind">The provider family.</param>
    /// <param name="providerInstanceId">The opaque instance scope. It must not contain a secret.</param>
    /// <param name="displayName">An optional human-readable name.</param>
    /// <param name="applicationVersion">An optional observed application version.</param>
    /// <param name="capabilities">Optional derived capability names.</param>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not a defined provider.</exception>
    /// <exception cref="ArgumentException">The instance identifier is empty.</exception>
    public ArrProvider(
        ArrProviderKind kind,
        string providerInstanceId,
        string? displayName = null,
        string? applicationVersion = null,
        IEnumerable<string>? capabilities = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Arr provider kind.");
        }

        if (string.IsNullOrWhiteSpace(providerInstanceId))
        {
            throw new ArgumentException("A provider instance identifier is required.", nameof(providerInstanceId));
        }

        Kind = kind;
        ProviderInstanceId = providerInstanceId;
        DisplayName = displayName;
        ApplicationVersion = applicationVersion;
        ApiContract = V3ApiContract;
        Capabilities = capabilities is null
            ? NoCapabilities
            : capabilities.Where(capability => !string.IsNullOrWhiteSpace(capability)).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the provider family.
    /// </summary>
    public ArrProviderKind Kind { get; }

    /// <summary>
    /// Gets the opaque provider-instance scope. It is generated from the
    /// configured connection and never contains a credential.
    /// </summary>
    public string ProviderInstanceId { get; }

    /// <summary>
    /// Gets the human-readable name when observed.
    /// </summary>
    public string? DisplayName { get; }

    /// <summary>
    /// Gets the observed application version when available.
    /// </summary>
    public string? ApplicationVersion { get; }

    /// <summary>
    /// Gets the provider API contract.
    /// </summary>
    public string ApiContract { get; }

    /// <summary>
    /// Gets the derived capability names. Unknown capabilities remain absent.
    /// </summary>
    public IReadOnlySet<string> Capabilities { get; }
}
