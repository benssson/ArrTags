using System;

namespace ArrTags.Webhooks;

/// <summary>
/// The bounded outcome of reading an inbound webhook request body.
/// </summary>
public sealed class WebhookBodyReadResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookBodyReadResult"/> class.
    /// </summary>
    /// <param name="tooLarge">Whether the declared or actual body exceeded the bound.</param>
    /// <param name="bytes">The bounded body bytes when the read succeeded.</param>
    public WebhookBodyReadResult(bool tooLarge, ReadOnlyMemory<byte> bytes)
    {
        TooLarge = tooLarge;
        Bytes = bytes;
    }

    /// <summary>
    /// Gets a value indicating whether the declared or actual body exceeded the
    /// configured bound. No body bytes are retained in that case.
    /// </summary>
    public bool TooLarge { get; }

    /// <summary>
    /// Gets the bounded body bytes. Empty when <see cref="TooLarge"/> is set.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes { get; }
}
