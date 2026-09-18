namespace ArrTags.Providers;

/// <summary>
/// The certificate validation policy for one Arr connection. Validation is
/// strict unless an explicit, connection-scoped exception is configured.
/// </summary>
public enum ArrTlsPolicy
{
    /// <summary>
    /// Certificate validation is enforced.
    /// </summary>
    Strict,

    /// <summary>
    /// Certificate validation is relaxed for this connection only.
    /// </summary>
    AllowInsecure,
}
