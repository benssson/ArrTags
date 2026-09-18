namespace ArrTags.Metadata;

/// <summary>
/// A normalized audio feature derived from an Arr file's reported audio
/// information. A feature is added only when the source data supports it; an
/// absent feature is not a confirmed negative.
/// </summary>
public enum ArrAudioFeature
{
    /// <summary>
    /// Dolby Atmos.
    /// </summary>
    Atmos,

    /// <summary>
    /// DTS.
    /// </summary>
    Dts,

    /// <summary>
    /// DTS-HD.
    /// </summary>
    DtsHd,

    /// <summary>
    /// DTS:X.
    /// </summary>
    DtsX,
}
