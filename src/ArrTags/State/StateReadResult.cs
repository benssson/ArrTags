namespace ArrTags.State;

/// <summary>
/// An immutable state read outcome. The status distinguishes a missing record
/// from an invalid cache record that was discarded and from an invalid
/// authoritative record that was quarantined.
/// </summary>
/// <typeparam name="T">The record payload type.</typeparam>
public sealed class StateReadResult<T>
    where T : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StateReadResult{T}"/> class.
    /// </summary>
    /// <param name="status">The read status.</param>
    /// <param name="value">The deserialized payload when found.</param>
    /// <param name="reason">A bounded, non-secret explanation for an invalid record.</param>
    public StateReadResult(StateReadStatus status, T? value, string? reason)
    {
        Status = status;
        Value = value;
        Reason = reason;
    }

    /// <summary>
    /// Gets the read status.
    /// </summary>
    public StateReadStatus Status { get; }

    /// <summary>
    /// Gets the deserialized payload when the status is <see cref="StateReadStatus.Found"/>.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation for an invalid record.
    /// </summary>
    public string? Reason { get; }
}
