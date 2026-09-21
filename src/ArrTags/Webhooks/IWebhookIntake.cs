namespace ArrTags.Webhooks;

/// <summary>
/// The narrow, non-blocking submit boundary used by the webhook controller. The
/// controller authenticates, bounds, and parses a request, then hands the
/// bounded <see cref="WebhookEvent"/> to this boundary and returns immediately.
/// Implementations must never perform external I/O, provider reads, rendering,
/// or image writes, must never block the request thread on queued work, and must
/// never throw for ordinary coalescing or overflow. The production
/// implementation is <see cref="WebhookIntake"/>.
/// </summary>
public interface IWebhookIntake
{
    /// <summary>
    /// Gets the bounded capacity of the intake.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the current number of pending events, safe to expose as a bounded
    /// diagnostic.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Attempts to submit a bounded webhook event without blocking. A duplicate
    /// or replayed delivery within the coalescing window is suppressed, and an
    /// event that would exceed the bounded capacity is dropped, so a webhook
    /// burst can never grow without bound or block the request.
    /// </summary>
    /// <param name="webhookEvent">The bounded, secret-free webhook event.</param>
    /// <returns><see langword="true"/> when the event was accepted.</returns>
    bool TrySubmit(WebhookEvent webhookEvent);
}
