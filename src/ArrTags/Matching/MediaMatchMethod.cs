namespace ArrTags.Matching;

/// <summary>
/// How a match was established. The method is recorded as evidence and is part
/// of the deterministic match fingerprint. <see cref="None"/> is used when no
/// match was accepted.
/// </summary>
public enum MediaMatchMethod
{
    /// <summary>
    /// No matching method was applied or no match was accepted.
    /// </summary>
    None,

    /// <summary>
    /// Matching used a validated external provider identifier such as TVDB,
    /// TMDb, or IMDb.
    /// </summary>
    ProviderId,

    /// <summary>
    /// Matching used validated season and episode numbers after a series match.
    /// </summary>
    Number,

    /// <summary>
    /// Matching used a configured, normalized path mapping.
    /// </summary>
    ConfiguredPath,

    /// <summary>
    /// Matching was established by explicit manual selection.
    /// </summary>
    Manual,
}
