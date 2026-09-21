using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ArrTags.Webhooks;

/// <summary>
/// Reads an inbound webhook request body under a hard byte bound. A declared or
/// actual length above the bound is rejected before the whole body is buffered,
/// so a webhook can never force an unbounded allocation (ADR-012).
/// </summary>
public static class WebhookBodyReader
{
    private const int ChunkBytes = 8192;

    /// <summary>
    /// Reads the request body into a bounded buffer.
    /// </summary>
    /// <param name="request">The inbound HTTP request.</param>
    /// <param name="maxBytes">The configured maximum payload size in bytes.</param>
    /// <param name="cancellationToken">The request cancellation signal.</param>
    /// <returns>The bounded read outcome.</returns>
    /// <exception cref="ArgumentNullException">The request is <see langword="null"/>.</exception>
    public static async Task<WebhookBodyReadResult> ReadAsync(
        HttpRequest request,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (maxBytes < 1)
        {
            maxBytes = 1;
        }

        if (request.ContentLength is long declared && declared > maxBytes)
        {
            return new WebhookBodyReadResult(true, ReadOnlyMemory<byte>.Empty);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        long total = 0;

        while (true)
        {
            var read = await request.Body.ReadAsync(chunk.AsMemory(0, ChunkBytes), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return new WebhookBodyReadResult(true, ReadOnlyMemory<byte>.Empty);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return new WebhookBodyReadResult(false, buffer.ToArray());
    }
}
