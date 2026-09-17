namespace ArrTags.State;

/// <summary>
/// Factory helpers for <see cref="StateReadResult{T}"/>.
/// </summary>
public static class StateResults
{
    /// <summary>
    /// Creates a found result.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="value">The deserialized payload.</param>
    /// <returns>A found result.</returns>
    public static StateReadResult<T> Found<T>(T value)
        where T : class
    {
        return new StateReadResult<T>(StateReadStatus.Found, value, null);
    }

    /// <summary>
    /// Creates a missing result.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <returns>A missing result.</returns>
    public static StateReadResult<T> Missing<T>()
        where T : class
    {
        return new StateReadResult<T>(StateReadStatus.Missing, null, null);
    }

    /// <summary>
    /// Creates a discarded-cache result.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A discarded result.</returns>
    public static StateReadResult<T> Discarded<T>(string reason)
        where T : class
    {
        return new StateReadResult<T>(StateReadStatus.InvalidDiscarded, null, reason);
    }

    /// <summary>
    /// Creates a quarantined-authoritative result.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A quarantined result.</returns>
    public static StateReadResult<T> Quarantined<T>(string reason)
        where T : class
    {
        return new StateReadResult<T>(StateReadStatus.InvalidQuarantined, null, reason);
    }
}
