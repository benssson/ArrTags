namespace ArrTags.Providers;

/// <summary>
/// A bounded provider read outcome. A successful read carries the parsed value;
/// a failed read carries a redacted <see cref="ArrProviderError"/> and never a
/// provider payload or credential.
/// </summary>
/// <typeparam name="T">The provider-boundary read value type.</typeparam>
public sealed class ArrProviderReadResult<T>
{
    internal ArrProviderReadResult(bool isSuccess, T? value, ArrProviderError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Gets a value indicating whether the read succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the parsed value when the read succeeded.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets the redacted failure when the read did not succeed.
    /// </summary>
    public ArrProviderError? Error { get; }
}
