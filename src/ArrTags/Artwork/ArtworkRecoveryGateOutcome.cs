namespace ArrTags.Artwork;

/// <summary>
/// The bounded outcome of gating new work on the recovery of a non-terminal
/// artwork operation for one item/image surface. Only
/// <see cref="NoOperation"/>, <see cref="AlreadyTerminal"/>, and
/// <see cref="Recovered"/> permit new work to proceed; the other values are
/// fail-closed and defer or refuse the work.
/// </summary>
public enum ArtworkRecoveryGateOutcome
{
    /// <summary>No durable artwork operation exists for the subject; new work may proceed.</summary>
    NoOperation,

    /// <summary>The durable artwork operation already reached a terminal outcome; new work may proceed.</summary>
    AlreadyTerminal,

    /// <summary>A non-terminal operation was recovered to a terminal outcome; new work may proceed.</summary>
    Recovered,

    /// <summary>Recovery did not reach a terminal outcome; new work must not proceed.</summary>
    Deferred,

    /// <summary>The durable record is invalid, corrupt, or inconsistent; new work must not proceed.</summary>
    Blocked,

    /// <summary>Recovery was cancelled; new work must not proceed.</summary>
    Cancelled,
}
