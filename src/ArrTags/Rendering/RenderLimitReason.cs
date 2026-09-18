namespace ArrTags.Rendering;

/// <summary>
/// The bounded, non-secret reason a render attempt is rejected or passed through
/// before decode, draw, or encode work. The values describe only pre-render
/// limit enforcement; they contain no provider payload, path, credential, or
/// item identity. <see cref="None"/> means the input was accepted.
/// </summary>
public enum RenderLimitReason
{
    /// <summary>
    /// The input is within every enforced limit.
    /// </summary>
    None = 0,

    /// <summary>
    /// The source descriptor is structurally unusable, for example a
    /// non-positive byte length or a non-positive dimension.
    /// </summary>
    MalformedSource,

    /// <summary>
    /// The source byte length exceeds the accepted source-artifact limit.
    /// </summary>
    SourceByteLimitExceeded,

    /// <summary>
    /// A decoded/oriented source dimension exceeds the accepted per-side
    /// dimension limit.
    /// </summary>
    SourceDimensionLimitExceeded,

    /// <summary>
    /// A planned output dimension exceeds the accepted per-side dimension limit.
    /// </summary>
    OutputDimensionLimitExceeded,

    /// <summary>
    /// The uncompressed RGBA output surface exceeds the accepted derived-artifact
    /// byte limit, so the render is rejected before the encoder allocates or
    /// writes a surface that could not fit the artifact budget.
    /// </summary>
    OutputByteLimitExceeded,
}
