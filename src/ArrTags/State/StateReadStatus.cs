namespace ArrTags.State;

/// <summary>
/// Describes the outcome of reading a versioned state record.
/// </summary>
public enum StateReadStatus
{
    /// <summary>
    /// A valid record was found.
    /// </summary>
    Found,

    /// <summary>
    /// No record exists. This is a normal condition.
    /// </summary>
    Missing,

    /// <summary>
    /// An invalid cache record was discarded and may be rebuilt.
    /// </summary>
    InvalidDiscarded,

    /// <summary>
    /// An invalid authoritative record was quarantined. It must not be treated
    /// as absent or replayed blindly.
    /// </summary>
    InvalidQuarantined,
}
