using System;

namespace ArrTags.Providers;

/// <summary>
/// The connection-scoped, typed identity of one Arr record and its current file.
/// Every identity is scoped to exactly one <see cref="ArrConnection"/>, so the
/// same local record or file identifier observed through two connections is
/// never the same identity. Concrete identities are never mixed or substituted
/// across providers.
/// </summary>
public abstract class ArrRecordIdentity : IEquatable<ArrRecordIdentity>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrRecordIdentity"/> class.
    /// </summary>
    /// <param name="connectionId">The connection scope for every contained local identifier.</param>
    /// <param name="providerKind">The provider family that selects the identity shape.</param>
    /// <exception cref="ArgumentNullException">The connection identifier is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The provider kind is not defined.</exception>
    protected ArrRecordIdentity(ArrConnectionId connectionId, ArrProviderKind providerKind)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind), providerKind, "Unknown Arr provider kind.");
        }

        ConnectionId = connectionId;
        ProviderKind = providerKind;
    }

    /// <summary>
    /// Gets the connection scope for every contained local identifier.
    /// </summary>
    public ArrConnectionId ConnectionId { get; }

    /// <summary>
    /// Gets the provider family that selects the concrete identity shape.
    /// </summary>
    public ArrProviderKind ProviderKind { get; }

    /// <summary>
    /// Determines whether this identity equals another identity of the same
    /// concrete type and connection scope.
    /// </summary>
    /// <param name="other">The other identity.</param>
    /// <returns><see langword="true"/> when the concrete identities are equal.</returns>
    public bool Equals(ArrRecordIdentity? other)
    {
        return other is not null
            && GetType() == other.GetType()
            && ProviderKind == other.ProviderKind
            && ConnectionId.Equals(other.ConnectionId)
            && EqualsCore(other);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as ArrRecordIdentity);
    }

    /// <inheritdoc />
    public abstract override int GetHashCode();

    /// <summary>
    /// Compares the concrete identity components after the shared type, provider
    /// kind, and connection scope have already been compared.
    /// </summary>
    /// <param name="other">The other identity of the same concrete type.</param>
    /// <returns><see langword="true"/> when the concrete components are equal.</returns>
    protected abstract bool EqualsCore(ArrRecordIdentity other);
}
