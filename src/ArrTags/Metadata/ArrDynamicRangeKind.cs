namespace ArrTags.Metadata;

/// <summary>
/// The normalized dynamic-range family reported by an Arr file. An absent or
/// unrecognized media-information value is <see cref="Unknown"/> and must not be
/// presented as a confirmed negative.
/// </summary>
public enum ArrDynamicRangeKind
{
    /// <summary>
    /// The dynamic range is not known.
    /// </summary>
    Unknown,

    /// <summary>
    /// Standard dynamic range was reported.
    /// </summary>
    Sdr,

    /// <summary>
    /// A generic high-dynamic-range value was reported.
    /// </summary>
    Hdr,

    /// <summary>
    /// HDR10 was reported.
    /// </summary>
    Hdr10,

    /// <summary>
    /// HDR10+ was reported.
    /// </summary>
    Hdr10Plus,

    /// <summary>
    /// Hybrid Log-Gamma was reported.
    /// </summary>
    Hlg,

    /// <summary>
    /// Dolby Vision was reported.
    /// </summary>
    DolbyVision,
}
