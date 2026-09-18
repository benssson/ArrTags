using System;
using ArrTags.Media;

namespace ArrTags.Matching;

/// <summary>
/// A candidate rule that compares one normalized external provider identifier,
/// such as TVDB, TMDb, or IMDb, between the Jellyfin identity and the provider
/// candidate. It is provider-neutral: the matching policy selects which
/// identifier keys apply and in what order.
/// </summary>
public sealed class ProviderIdMatchRule : CandidateMatchRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderIdMatchRule"/> class.
    /// </summary>
    /// <param name="providerIdKey">The normalized provider identifier key, such as <c>Tvdb</c> or <c>Tmdb</c>.</param>
    /// <exception cref="ArgumentException">The key is empty.</exception>
    public ProviderIdMatchRule(string providerIdKey)
        : base(MediaMatchMethod.ProviderId, providerIdKey)
    {
    }

    /// <inheritdoc />
    public override bool IsSatisfiedBy(MediaIdentity identity, MatchCandidate candidate, out string? matchedValue)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidate);

        matchedValue = null;
        if (!identity.ProviderIds.TryGetValue(EvidenceKey, out var required) || string.IsNullOrWhiteSpace(required))
        {
            return false;
        }

        if (!candidate.ProviderIds.TryGetValue(EvidenceKey, out var offered) || string.IsNullOrWhiteSpace(offered))
        {
            return false;
        }

        if (!string.Equals(required, offered, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        matchedValue = required.Trim();
        return true;
    }
}
