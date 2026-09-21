using System.Threading;
using System.Threading.Tasks;
using ArrTags.Media;
using ArrTags.Providers;

namespace ArrTags.Reconciliation;

/// <summary>
/// The provider-neutral reconciliation read boundary. One implementation exists
/// per Arr provider and composes the provider read client, the canonical
/// candidate assembly, the documented matching order, and the canonical
/// metadata mapping. It keeps provider DTO handling inside the provider layer
/// and returns a canonical, secret-free result.
/// </summary>
public interface IArrMetadataReader
{
    /// <summary>
    /// Gets the provider family this reader serves.
    /// </summary>
    ArrProviderKind Kind { get; }

    /// <summary>
    /// Reads the current provider state for one item through the supplied
    /// connection, matches it, and maps the matched record's metadata. The
    /// implementation must honor the supplied cancellation token.
    /// </summary>
    /// <param name="identity">The current Jellyfin subject identity.</param>
    /// <param name="connection">The resolved connection to read through.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The bounded read and match outcome.</returns>
    Task<ArrMetadataReadResult> ReadAsync(
        MediaIdentity identity,
        ArrConnection connection,
        CancellationToken cancellationToken);
}
