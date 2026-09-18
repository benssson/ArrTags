namespace ArrTags.Providers;

/// <summary>
/// The observed health of one Arr connection. Health is derived and cached by
/// ArrTags; it is never persisted as provider data.
/// </summary>
public enum ArrConnectionHealth
{
    /// <summary>
    /// The connection has not been probed or cannot be classified.
    /// </summary>
    Unknown,

    /// <summary>
    /// The connection responded successfully and is usable.
    /// </summary>
    Healthy,

    /// <summary>
    /// The connection is unreachable or returned a transient failure.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The provider rejected the configured credentials.
    /// </summary>
    AuthenticationFailed,

    /// <summary>
    /// The provider responded but its version, contract, or required fields are
    /// unsupported.
    /// </summary>
    Incompatible,
}
