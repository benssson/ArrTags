namespace ArrTags.Configuration;

/// <summary>
/// The bounded per-plugin log verbosity (ADR-020 clause 2). It selects how much
/// ArrTags writes through the host logging pipeline; it never changes the host
/// log level, and it is excluded from the renderer and configuration output
/// fingerprints and from <see cref="Rendering.RenderVersion"/> because it is not
/// output-affecting (ADR-020 clause 5).
/// </summary>
public enum LogVerbosity
{
    /// <summary>ArrTags writes no log records.</summary>
    Off,

    /// <summary>ArrTags writes error records and above.</summary>
    Error,

    /// <summary>ArrTags writes warning records and above. This is the default so normal operation stays quiet.</summary>
    Warning,

    /// <summary>ArrTags writes informational records and above.</summary>
    Information,

    /// <summary>ArrTags writes debug records and above.</summary>
    Debug,

    /// <summary>ArrTags writes every record, including trace.</summary>
    Trace,
}
