namespace ArrTags.Rendering;

/// <summary>
/// The bounded, non-secret reason a render failed and produced no artifact. Every
/// value is safe to record in diagnostics: none contains a provider payload,
/// filesystem path, credential, or image content.
/// </summary>
public enum RenderFailureReason
{
    /// <summary>
    /// The request was structurally unusable.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// The source descriptor was malformed.
    /// </summary>
    MalformedSource,

    /// <summary>
    /// The source byte length exceeded the accepted source-artifact limit.
    /// </summary>
    SourceByteLimitExceeded,

    /// <summary>
    /// A source dimension exceeded the accepted per-side dimension limit.
    /// </summary>
    SourceDimensionLimitExceeded,

    /// <summary>
    /// A planned output dimension exceeded the accepted per-side dimension limit.
    /// </summary>
    OutputDimensionLimitExceeded,

    /// <summary>
    /// The planned or encoded output exceeded the accepted derived-artifact byte
    /// limit.
    /// </summary>
    OutputByteLimitExceeded,

    /// <summary>
    /// A configured style color was missing or not parseable.
    /// </summary>
    ColorPolicyInvalid,

    /// <summary>
    /// A configured style failed the required text/background contrast ratio.
    /// </summary>
    ContrastTooLow,

    /// <summary>
    /// The source bytes could not be decoded.
    /// </summary>
    DecodeFailed,

    /// <summary>
    /// The source bytes use an unsupported image semantic.
    /// </summary>
    UnsupportedInput,

    /// <summary>
    /// The bundled font resource was missing.
    /// </summary>
    FontUnavailable,

    /// <summary>
    /// The bundled font resource could not be used to create a typeface.
    /// </summary>
    FontInvalid,

    /// <summary>
    /// The layout stage could not produce a valid surface.
    /// </summary>
    LayoutFailed,

    /// <summary>
    /// The PNG encoder could not produce a complete artifact.
    /// </summary>
    EncodeFailed,

    /// <summary>
    /// A required native renderer asset could not be loaded.
    /// </summary>
    NativeAssetUnavailable,

    /// <summary>
    /// An otherwise unclassified renderer error occurred. No exception detail is
    /// exposed.
    /// </summary>
    RenderError,

    /// <summary>
    /// The attempt was cancelled and no partial output was produced.
    /// </summary>
    Cancelled,
}
