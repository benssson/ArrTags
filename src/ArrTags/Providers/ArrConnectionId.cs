using System;

namespace ArrTags.Providers;

/// <summary>
/// A stable, non-secret identifier for one configured Sonarr or Radarr
/// connection. The identifier scopes every Arr local record and file identity so
/// the same numeric ID observed through two connections can never be mistaken
/// for the same item.
/// </summary>
/// <remarks>
/// V1 configures at most one connection per provider kind. The identity is
/// derived from the provider kind and the normalized base URL; the API key is
/// deliberately excluded so rotating a key does not change the identity and a
/// secret can never appear in a cache key or fingerprint. An unconfigured base
/// URL yields the provider name alone.
/// </remarks>
public sealed class ArrConnectionId : IEquatable<ArrConnectionId>
{
    private ArrConnectionId(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the opaque identifier value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates the connection identifier for a provider kind and configured base
    /// URL. Only the scheme, host, port, and path participate; user information
    /// and the API key never participate.
    /// </summary>
    /// <param name="kind">The provider kind.</param>
    /// <param name="baseUrl">The configured base URL, or <see langword="null"/> when unconfigured.</param>
    /// <returns>The stable connection identifier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not a defined provider.</exception>
    public static ArrConnectionId For(ArrProviderKind kind, string? baseUrl)
    {
        var name = kind.ToApiName();
        var normalized = NormalizeBaseUrl(baseUrl);
        return new ArrConnectionId(normalized.Length == 0 ? name : name + ":" + normalized);
    }

    /// <summary>
    /// Determines whether this identifier equals another identifier.
    /// </summary>
    /// <param name="other">The other identifier.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public bool Equals(ArrConnectionId? other)
    {
        return other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as ArrConnectionId);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.TrimEnd('/');
        }

        var server = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        var path = uri.AbsolutePath;
        if (path.Length <= 1)
        {
            path = string.Empty;
        }
        else
        {
            path = path.TrimEnd('/');
        }

        return server + path;
    }
}
