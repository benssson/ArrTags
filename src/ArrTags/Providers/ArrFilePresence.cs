namespace ArrTags.Providers;

/// <summary>
/// The explicit presence of a current Arr file. Absence is a value, never an
/// error, and must not be confused with an unknown file identity.
/// </summary>
public enum ArrFilePresence
{
    /// <summary>
    /// The Arr record has no current imported file.
    /// </summary>
    Absent,

    /// <summary>
    /// The Arr record has a current imported file with a local identifier.
    /// </summary>
    Present,
}
