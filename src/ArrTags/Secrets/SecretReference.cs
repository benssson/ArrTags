using System;

namespace ArrTags.Secrets;

/// <summary>
/// A safe, typed reference to one credential slot. A reference identifies where
/// a secret may be resolved but never contains the secret value, a vault
/// address, or a provider URL. References are stable across secret rotation.
/// </summary>
public sealed class SecretReference : IEquatable<SecretReference>
{
    private SecretReference(SecretPurpose purpose, string slotId)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown secret purpose.");
        }

        if (string.IsNullOrWhiteSpace(slotId))
        {
            throw new ArgumentException("A secret slot identifier is required.", nameof(slotId));
        }

        Purpose = purpose;
        SlotId = slotId;
    }

    /// <summary>
    /// Gets the Sonarr API-key slot.
    /// </summary>
    public static SecretReference SonarrApiKey { get; } = new SecretReference(SecretPurpose.ArrApiKey, "sonarr");

    /// <summary>
    /// Gets the Radarr API-key slot.
    /// </summary>
    public static SecretReference RadarrApiKey { get; } = new SecretReference(SecretPurpose.ArrApiKey, "radarr");

    /// <summary>
    /// Gets the inbound webhook-authentication slot.
    /// </summary>
    public static SecretReference WebhookAuthentication { get; } = new SecretReference(SecretPurpose.WebhookAuthentication, "webhook");

    /// <summary>
    /// Gets the credential purpose. Purposes are not interchangeable.
    /// </summary>
    public SecretPurpose Purpose { get; }

    /// <summary>
    /// Gets the stable slot identifier. It never contains the secret value.
    /// </summary>
    public string SlotId { get; }

    /// <inheritdoc />
    public bool Equals(SecretReference? other)
    {
        return other is not null
            && Purpose == other.Purpose
            && string.Equals(SlotId, other.SlotId, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as SecretReference);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Purpose, StringComparer.Ordinal.GetHashCode(SlotId));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormattableString.Invariant($"{Purpose}:{SlotId}");
    }
}
