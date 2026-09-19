namespace ArrTags.Rendering;

/// <summary>
/// The bounded result of inspecting a source image's embedded color profile under
/// ADR-010. <see cref="None"/> means the recognized container carried no
/// embedded profile and the input is treated as sRGB. <see cref="Supported"/>
/// means an embedded profile was found and could be parsed into a color space the
/// pinned renderer converts to sRGB. <see cref="Invalid"/> means an embedded
/// profile was found but is malformed or unsupported, so the render must fail
/// closed rather than guess sRGB.
/// </summary>
internal enum SourceColorProfileKind
{
    /// <summary>
    /// No embedded profile was found; the input is treated as sRGB.
    /// </summary>
    None = 0,

    /// <summary>
    /// A supported embedded profile was found and can be converted to sRGB.
    /// </summary>
    Supported = 1,

    /// <summary>
    /// An embedded profile was found but is malformed or unsupported.
    /// </summary>
    Invalid = 2,
}
