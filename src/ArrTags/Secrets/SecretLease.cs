using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ArrTags.Secrets;

/// <summary>
/// A short-lived, disposable handle to one resolved secret. A lease is never
/// serializable, is never persisted, and must never appear in logs, exceptions,
/// or canonical state. Its diagnostic string representation excludes the value.
/// </summary>
public sealed class SecretLease : IDisposable
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    private string _value;
    private bool _disposed;

    internal SecretLease(SecretReference reference, string value)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Reference = reference;
        _value = value;
    }

    /// <summary>
    /// Gets the safe slot this lease was resolved from.
    /// </summary>
    public SecretReference Reference { get; }

    /// <summary>
    /// Gets a value indicating whether this lease has been released.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Applies an Arr API-key lease to the request as an <c>X-Api-Key</c> header.
    /// The key is never placed in the URL or query string.
    /// </summary>
    /// <param name="request">The outbound provider request.</param>
    /// <exception cref="ArgumentNullException">The request is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The lease is disposed or not an API key.</exception>
    public void ApplyTo(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (Reference.Purpose != SecretPurpose.ArrApiKey)
        {
            throw new InvalidOperationException("This lease is not an Arr API key.");
        }

        request.Headers.Remove(ApiKeyHeaderName);
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, _value);
    }

    /// <summary>
    /// Compares a webhook-authentication candidate in constant time.
    /// </summary>
    /// <param name="candidate">The untrusted candidate value.</param>
    /// <returns><see langword="true"/> when the candidate matches.</returns>
    /// <exception cref="InvalidOperationException">The lease is disposed or not a webhook secret.</exception>
    public bool Matches(string? candidate)
    {
        ThrowIfDisposed();

        if (Reference.Purpose != SecretPurpose.WebhookAuthentication)
        {
            throw new InvalidOperationException("This lease is not a webhook authentication secret.");
        }

        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(_value);
        var actual = Encoding.UTF8.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _value = string.Empty;
        _disposed = true;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormattableString.Invariant($"SecretLease({Reference})");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new InvalidOperationException("The secret lease has been released.");
        }
    }
}
