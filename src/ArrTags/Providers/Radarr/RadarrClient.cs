using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Secrets;

namespace ArrTags.Providers.Radarr;

/// <summary>
/// The read-only Radarr v3 client. It uses Jellyfin's pooled HTTP client for
/// transport, acquires a version-matched API-key lease immediately before each
/// request, and maps every failure into a bounded, redacted
/// <see cref="ArrProviderError"/>. It never logs or returns secret material.
/// </summary>
public sealed class RadarrClient : IRadarrReadClient
{
    private const string SystemStatusPath = "api/v3/system/status";
    private const string MoviesPath = "api/v3/movie";
    private const string MovieFilesPath = "api/v3/moviefile";
    private const int ReadBufferSize = 81920;

    private static readonly string[] Capabilities = { "movie", "movieFile" };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ArrConnection _connection;
    private readonly IArrHttpClientFactory _httpClientFactory;
    private readonly IPluginSecretResolver _secretResolver;
    private readonly OperationalLimits _limits;

    /// <summary>
    /// Initializes a new instance of the <see cref="RadarrClient"/> class.
    /// </summary>
    /// <param name="connection">The canonical Radarr connection to read through.</param>
    /// <param name="httpClientFactory">The secret-free transport factory.</param>
    /// <param name="secretResolver">The versioned credential boundary.</param>
    /// <param name="limits">The operational limits snapshot.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public RadarrClient(
        ArrConnection connection,
        IArrHttpClientFactory httpClientFactory,
        IPluginSecretResolver secretResolver,
        OperationalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(secretResolver);
        ArgumentNullException.ThrowIfNull(limits);

        _connection = connection;
        _httpClientFactory = httpClientFactory;
        _secretResolver = secretResolver;
        _limits = limits.Clone();
    }

    /// <inheritdoc />
    public ArrProviderKind Kind => ArrProviderKind.Radarr;

    /// <inheritdoc />
    public ArrConnection Connection => _connection;

    /// <inheritdoc />
    public async Task<ArrConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var result = await ReadJsonAsync<RadarrSystemStatusResource>(
            BuildRequestUri(SystemStatusPath),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ArrConnectionProbeResult.Failed(ToHealth(result.Error!), result.Error!);
        }

        var status = result.Value!;
        if (!string.Equals(status.AppName, "Radarr", StringComparison.OrdinalIgnoreCase))
        {
            return ArrConnectionProbeResult.Failed(
                ArrConnectionHealth.Incompatible,
                new ArrProviderError(
                    ArrProviderErrorCode.ProviderIncompatible,
                    ArrErrorRetryability.Never,
                    "The provider did not identify itself as Radarr."));
        }

        var provider = new ArrProvider(
            ArrProviderKind.Radarr,
            _connection.Provider.ProviderInstanceId,
            string.IsNullOrWhiteSpace(status.InstanceName) ? "Radarr" : status.InstanceName,
            status.Version,
            Capabilities);

        return ArrConnectionProbeResult.Healthy(provider);
    }

    /// <inheritdoc />
    public async Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        var result = await ReadJsonAsync<List<RadarrMovieResource>>(
            BuildRequestUri(MoviesPath),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(result.Value!)
            : ArrProviderResults.Failure<IReadOnlyList<RadarrMovieResource>>(result.Error!);
    }

    /// <inheritdoc />
    public async Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(int movieId, CancellationToken cancellationToken)
    {
        if (movieId <= 0)
        {
            return ArrProviderResults.Failure<IReadOnlyList<RadarrMovieFileResource>>(
                new ArrProviderError(
                    ArrProviderErrorCode.InvalidResponse,
                    ArrErrorRetryability.Never,
                    "A positive Radarr movie identifier is required."));
        }

        var relativePath = MovieFilesPath + "?movieId=" + movieId.ToString(CultureInfo.InvariantCulture);
        var result = await ReadJsonAsync<List<RadarrMovieFileResource>>(
            BuildRequestUri(relativePath),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(result.Value!)
            : ArrProviderResults.Failure<IReadOnlyList<RadarrMovieFileResource>>(result.Error!);
    }

    /// <inheritdoc />
    public async Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(
        IReadOnlyList<int> movieIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movieIds);

        if (movieIds.Count == 0)
        {
            return ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(
                Array.Empty<RadarrMovieFileResource>());
        }

        foreach (var movieId in movieIds)
        {
            if (movieId <= 0)
            {
                return ArrProviderResults.Failure<IReadOnlyList<RadarrMovieFileResource>>(
                    new ArrProviderError(
                        ArrProviderErrorCode.InvalidResponse,
                        ArrErrorRetryability.Never,
                        "A positive Radarr movie identifier is required."));
            }
        }

        // The repeatable movieId selector grows the request line with the number
        // of ids, so the ids are chunked into bounded requests rather than one
        // unbounded query that a request-line limit could reject. Each chunk is
        // still a single bulk request; the caller's list is never split per item.
        var batchSize = _limits.ReconciliationBatchSize < 1 ? 1 : _limits.ReconciliationBatchSize;
        var files = new List<RadarrMovieFileResource>(movieIds.Count);
        for (var offset = 0; offset < movieIds.Count; offset += batchSize)
        {
            var count = Math.Min(batchSize, movieIds.Count - offset);
            var batch = new int[count];
            for (var index = 0; index < count; index++)
            {
                batch[index] = movieIds[offset + index];
            }

            var result = await ReadJsonAsync<List<RadarrMovieFileResource>>(
                BuildRequestUri(BuildMovieFilesPath(batch)!),
                cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess || result.Value is null)
            {
                return ArrProviderResults.Failure<IReadOnlyList<RadarrMovieFileResource>>(result.Error!);
            }

            files.AddRange(result.Value!);
        }

        return ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(files);
    }

    /// <summary>
    /// Builds the repeatable <c>movieId</c> query for the bulk movie-file read,
    /// or <see langword="null"/> when any identifier is not positive.
    /// </summary>
    private static string? BuildMovieFilesPath(IReadOnlyList<int> movieIds)
    {
        var builder = new StringBuilder(MovieFilesPath);
        for (var index = 0; index < movieIds.Count; index++)
        {
            var movieId = movieIds[index];
            if (movieId <= 0)
            {
                return null;
            }

            builder
                .Append(index == 0 ? '?' : '&')
                .Append("movieId=")
                .Append(movieId.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private async Task<ArrProviderReadResult<T>> ReadJsonAsync<T>(Uri requestUri, CancellationToken cancellationToken)
    {
        if (!_connection.Enabled)
        {
            return ArrProviderResults.Failure<T>(new ArrProviderError(
                ArrProviderErrorCode.ProviderUnavailable,
                ArrErrorRetryability.Never,
                "The connection is disabled."));
        }

        if (!_secretResolver.TryAcquire(_connection.ApiKeyReference, _connection.ConfigurationVersion, out var lease))
        {
            return ArrProviderResults.Failure<T>(new ArrProviderError(
                ArrProviderErrorCode.AuthenticationFailed,
                ArrErrorRetryability.AfterConfiguration,
                "The configured API key is unavailable for the current configuration."));
        }

        using (lease)
        using (var client = _httpClientFactory.CreateClient(_connection))
        {
            return await SendAsync<T>(client, lease, requestUri, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ArrProviderReadResult<T>> SendAsync<T>(
        HttpClient client,
        SecretLease lease,
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        var maxAttempts = _limits.TransientRetryCount + 1;
        ArrProviderError? lastError = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(ComputeBackoff(attempt - 1), cancellationToken).ConfigureAwait(false);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            lease.ApplyTo(request);

            try
            {
                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                var statusError = ClassifyStatus(response.StatusCode);
                if (statusError is null)
                {
                    return await ReadContentAsync<T>(response.Content, cancellationToken).ConfigureAwait(false);
                }

                if (statusError.Retryability != ArrErrorRetryability.Later || attempt == maxAttempts - 1)
                {
                    return ArrProviderResults.Failure<T>(statusError);
                }

                lastError = statusError;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                lastError = new ArrProviderError(
                    ArrProviderErrorCode.ProviderUnavailable,
                    ArrErrorRetryability.Later,
                    "The provider request timed out.");

                if (attempt == maxAttempts - 1)
                {
                    break;
                }
            }
            catch (HttpRequestException)
            {
                lastError = new ArrProviderError(
                    ArrProviderErrorCode.ProviderUnavailable,
                    ArrErrorRetryability.Later,
                    "The provider could not be reached.");

                if (attempt == maxAttempts - 1)
                {
                    break;
                }
            }
        }

        return ArrProviderResults.Failure<T>(lastError ?? new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider request failed."));
    }

    private async Task<ArrProviderReadResult<T>> ReadContentAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedBytesAsync(content, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return ArrProviderResults.Failure<T>(new ArrProviderError(
                ArrProviderErrorCode.InvalidResponse,
                ArrErrorRetryability.Never,
                "The provider response exceeded the configured size limit."));
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
            return value is null
                ? ArrProviderResults.Failure<T>(new ArrProviderError(
                    ArrProviderErrorCode.InvalidResponse,
                    ArrErrorRetryability.Never,
                    "The provider response was empty."))
                : ArrProviderResults.Success(value);
        }
        catch (JsonException)
        {
            return ArrProviderResults.Failure<T>(new ArrProviderError(
                ArrProviderErrorCode.InvalidResponse,
                ArrErrorRetryability.Never,
                "The provider response was not valid JSON."));
        }
    }

    private async Task<byte[]?> ReadBoundedBytesAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var limit = _limits.ProviderResponseLimitBytes;
        if (content.Headers.ContentLength is long length && length > limit)
        {
            return null;
        }

        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadBufferSize];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private TimeSpan ComputeBackoff(int attemptIndex)
    {
        var seconds = _limits.RetryBackoffInitialSeconds * Math.Pow(_limits.RetryBackoffFactor, attemptIndex);
        return TimeSpan.FromSeconds(Math.Min(seconds, _limits.RetryBackoffMaxSeconds));
    }

    private Uri BuildRequestUri(string relativePath)
    {
        var baseUrl = _connection.BaseUrl.TrimEnd('/');
        return new Uri(baseUrl + "/" + relativePath, UriKind.Absolute);
    }

    private static ArrProviderError? ClassifyStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and < 300)
        {
            return null;
        }

        return code switch
        {
            401 or 403 => new ArrProviderError(
                ArrProviderErrorCode.AuthenticationFailed,
                ArrErrorRetryability.AfterConfiguration,
                "The provider rejected the configured credentials."),
            404 => new ArrProviderError(
                ArrProviderErrorCode.ProviderIncompatible,
                ArrErrorRetryability.AfterConfiguration,
                "The provider API endpoint was not found; verify the configured base URL."),
            400 or 422 => new ArrProviderError(
                ArrProviderErrorCode.InvalidResponse,
                ArrErrorRetryability.Never,
                "The provider rejected the request as invalid."),
            409 => new ArrProviderError(
                ArrProviderErrorCode.ProviderUnavailable,
                ArrErrorRetryability.Later,
                "The provider reported a temporary conflict."),
            429 => new ArrProviderError(
                ArrProviderErrorCode.ProviderUnavailable,
                ArrErrorRetryability.Later,
                "The provider is rate limiting requests."),
            >= 500 => new ArrProviderError(
                ArrProviderErrorCode.ProviderUnavailable,
                ArrErrorRetryability.Later,
                "The provider returned a server error."),
            _ => new ArrProviderError(
                ArrProviderErrorCode.InvalidResponse,
                ArrErrorRetryability.Never,
                "The provider returned an unexpected response status."),
        };
    }

    private static ArrConnectionHealth ToHealth(ArrProviderError error)
    {
        return error.Code switch
        {
            ArrProviderErrorCode.AuthenticationFailed => ArrConnectionHealth.AuthenticationFailed,
            ArrProviderErrorCode.ProviderUnavailable => ArrConnectionHealth.Unavailable,
            ArrProviderErrorCode.ProviderIncompatible => ArrConnectionHealth.Incompatible,
            _ => ArrConnectionHealth.Unknown,
        };
    }
}
