using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ArrTags.Providers;

/// <summary>
/// Builds Arr transport clients from Jellyfin's pooled <see cref="IHttpClientFactory"/>.
/// The named client is selected from the connection's provider kind and TLS
/// policy, then configured with the connection base URL, timeout, and JSON
/// accept header. No secret value is read or attached.
/// </summary>
public sealed class ArrHttpClientFactory : IArrHttpClientFactory
{
    private const string JsonMediaType = "application/json";

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrHttpClientFactory"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's standard HTTP client factory.</param>
    /// <exception cref="ArgumentNullException">The factory is <see langword="null"/>.</exception>
    public ArrHttpClientFactory(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public HttpClient CreateClient(ArrConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!connection.Enabled)
        {
            throw new InvalidOperationException("A disabled Arr connection cannot create a client.");
        }

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri)
            || !IsHttpScheme(baseUri))
        {
            throw new InvalidOperationException("An enabled Arr connection requires an absolute HTTP or HTTPS base URL.");
        }

        var name = ArrHttpClientNames.For(connection.Provider.Kind, connection.TlsPolicy);
        var client = _httpClientFactory.CreateClient(name);
        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(connection.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        return client;
    }

    private static bool IsHttpScheme(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
