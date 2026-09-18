using System;

namespace ArrTags.Providers;

/// <summary>
/// Helpers for <see cref="ArrProviderKind"/>.
/// </summary>
public static class ArrProviderKindExtensions
{
    /// <summary>
    /// Gets the stable lower-case name used in connection identities and
    /// diagnostics.
    /// </summary>
    /// <param name="kind">The provider kind.</param>
    /// <returns>The stable provider name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not a defined provider.</exception>
    public static string ToApiName(this ArrProviderKind kind)
    {
        return kind switch
        {
            ArrProviderKind.Sonarr => "sonarr",
            ArrProviderKind.Radarr => "radarr",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Arr provider kind."),
        };
    }
}
