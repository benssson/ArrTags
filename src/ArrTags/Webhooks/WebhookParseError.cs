namespace ArrTags.Webhooks;

/// <summary>
/// The bounded outcome of parsing an inbound webhook payload. Every failure is
/// reported as one of these safe values so the controller can return a bounded
/// status code without exposing the payload, a header, or a parser exception.
/// </summary>
public enum WebhookParseError
{
    /// <summary>The payload parsed into a bounded webhook event.</summary>
    None,

    /// <summary>The request body was empty.</summary>
    Empty,

    /// <summary>The request body was not valid JSON.</summary>
    Malformed,

    /// <summary>The JSON was valid but not a well-formed webhook payload.</summary>
    WrongShape,
}
