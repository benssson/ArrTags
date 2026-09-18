using System.Net.Http;

namespace ArrTags.Providers;

/// <summary>
/// Creates transport clients for Arr connections from Jellyfin's pooled
/// <c>IHttpClientFactory</c>. The factory is secret-free: it applies only the
/// connection's base URL, finite timeout, JSON content negotiation, and TLS
/// policy. Credentials are added by the read client.
/// </summary>
public interface IArrHttpClientFactory
{
    /// <summary>
    /// Creates a configured client for one enabled connection. The underlying
    /// message handler is pooled by the host factory.
    /// </summary>
    /// <param name="connection">The canonical connection to configure.</param>
    /// <returns>A client scoped to the connection.</returns>
    HttpClient CreateClient(ArrConnection connection);
}
