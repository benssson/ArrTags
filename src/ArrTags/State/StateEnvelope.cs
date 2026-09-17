using System;

namespace ArrTags.State;

/// <summary>
/// The versioned storage envelope for one plugin state record. The envelope
/// carries integrity metadata and never contains credentials.
/// </summary>
public sealed class StateEnvelope
{
    /// <summary>
    /// Gets or sets the payload schema version.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets the record kind.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the record identifier within the kind.
    /// </summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the state authority.
    /// </summary>
    public StateAuthority Authority { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the record is terminal and
    /// eligible for retention cleanup.
    /// </summary>
    public bool Terminal { get; set; }

    /// <summary>
    /// Gets or sets the last update time in UTC.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the upper-case hexadecimal SHA-256 of the payload JSON.
    /// </summary>
    public string PayloadSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bounded payload JSON.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;
}
