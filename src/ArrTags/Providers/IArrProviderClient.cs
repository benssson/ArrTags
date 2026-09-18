using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Providers;

/// <summary>
/// The shared, read-only boundary implemented by every Arr client. A client is
/// bound to exactly one <see cref="ArrConnection"/> so every record it returns is
/// implicitly scoped to that connection.
/// </summary>
public interface IArrProviderClient
{
    /// <summary>
    /// Gets the provider family served by this client.
    /// </summary>
    ArrProviderKind Kind { get; }

    /// <summary>
    /// Gets the connection this client is scoped to.
    /// </summary>
    ArrConnection Connection { get; }

    /// <summary>
    /// Probes the connection and reports a bounded, redacted outcome. A probe is
    /// read-only and must never call an Arr write endpoint.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bounded probe result.</returns>
    Task<ArrConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken);
}
