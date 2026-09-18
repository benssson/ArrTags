using System;

namespace ArrTags.Providers;

/// <summary>
/// Creates bounded provider read outcomes. The factories live outside the
/// generic result type so static members are not declared on a generic type.
/// </summary>
public static class ArrProviderResults
{
    /// <summary>
    /// Creates a successful read outcome.
    /// </summary>
    /// <typeparam name="T">The provider-boundary read value type.</typeparam>
    /// <param name="value">The parsed value.</param>
    /// <returns>A successful result.</returns>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public static ArrProviderReadResult<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ArrProviderReadResult<T>(true, value, null);
    }

    /// <summary>
    /// Creates a failed read outcome.
    /// </summary>
    /// <typeparam name="T">The provider-boundary read value type.</typeparam>
    /// <param name="error">The bounded, redacted failure.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentNullException">The error is <see langword="null"/>.</exception>
    public static ArrProviderReadResult<T> Failure<T>(ArrProviderError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ArrProviderReadResult<T>(false, default, error);
    }
}
