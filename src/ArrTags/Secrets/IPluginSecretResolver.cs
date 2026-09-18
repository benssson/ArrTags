using System.Diagnostics.CodeAnalysis;

namespace ArrTags.Secrets;

/// <summary>
/// Resolves a safe <see cref="SecretReference"/> to a short-lived
/// <see cref="SecretLease"/> for exactly one configuration generation. The
/// resolver publishes immutable state and never exposes a secret value to
/// canonical models, queues, caches, or diagnostics.
/// </summary>
public interface IPluginSecretResolver
{
    /// <summary>
    /// Attempts to acquire a lease for a slot at the expected configuration
    /// version. A version mismatch, unknown reference, disabled slot, or missing
    /// value returns <see langword="false"/> instead of throwing so callers can
    /// discard or restart bounded work against the current snapshot.
    /// </summary>
    /// <param name="reference">The safe credential slot reference.</param>
    /// <param name="configurationVersion">The configuration generation the caller observed.</param>
    /// <param name="lease">The acquired lease when the call returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a matching lease was acquired.</returns>
    bool TryAcquire(SecretReference reference, long configurationVersion, [NotNullWhen(true)] out SecretLease? lease);
}
