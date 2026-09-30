using System;
using System.Globalization;
using ArrTags.Media;
using ArrTags.Providers;

namespace ArrTags.Matching;

/// <summary>
/// The explicit V1 episode-numbering policy for Sonarr episode matching,
/// resolving decision gate DG-4. Number fallback is deliberately conservative:
/// it applies only to regular, single episodes on both the Jellyfin and Sonarr
/// sides and never consults absolute or scene numbering. Specials (season zero)
/// and multi-episode spans require the episode TVDB identifier or a configured
/// path and are never matched by number alone. The policy is provider-neutral
/// and contains no provider DTO.
/// </summary>
/// <remarks>
/// The policy implements the documented order in <c>docs/architecture/07-sonarr-and-radarr-integration.md</c>
/// section 7, "Matching policy": episode TVDB id first, then exact season and
/// episode numbers after the series match. Absolute/scene numbering is recorded
/// as unsupported for V1 in <c>docs/decisions/ADR-007.md</c>.
/// </remarks>
public static class EpisodeNumberingPolicy
{
    /// <summary>
    /// The season number that represents specials. Specials are excluded from
    /// number fallback because Sonarr and Jellyfin can relocate them relative to
    /// their airing season.
    /// </summary>
    public const int SpecialSeasonNumber = 0;

    /// <summary>
    /// The evidence key recorded when season and episode numbers decide a match.
    /// </summary>
    public const string NumberEvidenceKey = "SeasonEpisode";

    /// <summary>
    /// Determines whether the Jellyfin identity is eligible for number fallback.
    /// </summary>
    /// <param name="identity">The Jellyfin-side episode identity.</param>
    /// <returns>
    /// <see langword="true"/> when the identity is a regular episode with a
    /// positive season and episode number and no multi-episode span.
    /// </returns>
    /// <exception cref="ArgumentNullException">The identity is <see langword="null"/>.</exception>
    public static bool IsEligible(MediaIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return identity.ItemType == MediaItemType.Episode
            && identity.SeasonNumber is int season
            && season > SpecialSeasonNumber
            && identity.EpisodeNumber is int episode
            && episode > 0
            && !HasMultiEpisodeSpan(identity.EpisodeNumber, identity.EpisodeNumberEnd);
    }

    /// <summary>
    /// Determines whether the provider candidate is eligible for number fallback.
    /// </summary>
    /// <param name="candidate">The provider-neutral episode candidate.</param>
    /// <returns>
    /// <see langword="true"/> when the candidate is a Sonarr episode with a
    /// positive season and episode number and no multi-episode span.
    /// </returns>
    /// <exception cref="ArgumentNullException">The candidate is <see langword="null"/>.</exception>
    public static bool IsEligible(MatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.ProviderKind == ArrProviderKind.Sonarr
            && candidate.SeasonNumber is int season
            && season > SpecialSeasonNumber
            && candidate.EpisodeNumber is int episode
            && episode > 0
            && !HasMultiEpisodeSpan(candidate.EpisodeNumber, candidate.EpisodeNumberEnd);
    }

    /// <summary>
    /// Determines whether the supplied episode numbers describe a multi-episode
    /// span. A span is present only when an end number is known and is greater
    /// than the start number; an end equal to the start is a single episode.
    /// </summary>
    /// <param name="episodeNumber">The start episode number when known.</param>
    /// <param name="episodeNumberEnd">The end episode number when known.</param>
    /// <returns><see langword="true"/> when the numbers span more than one episode.</returns>
    public static bool HasMultiEpisodeSpan(int? episodeNumber, int? episodeNumberEnd)
    {
        return episodeNumber is int start
            && episodeNumberEnd is int end
            && end > start;
    }

    /// <summary>
    /// Attempts to match a Jellyfin episode identity to a Sonarr episode
    /// candidate by exact season and episode number under the V1 policy.
    /// </summary>
    /// <param name="identity">The Jellyfin-side episode identity.</param>
    /// <param name="candidate">The Sonarr episode candidate.</param>
    /// <param name="matchedValue">The normalized season/episode value when the policy matched.</param>
    /// <returns><see langword="true"/> when both sides are eligible and their season and episode numbers are equal.</returns>
    /// <exception cref="ArgumentNullException">The identity or candidate is <see langword="null"/>.</exception>
    public static bool TryMatch(
        MediaIdentity identity,
        MatchCandidate candidate,
        out string? matchedValue)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidate);

        matchedValue = null;

        if (!IsEligible(identity) || !IsEligible(candidate))
        {
            return false;
        }

        if (identity.SeasonNumber != candidate.SeasonNumber
            || identity.EpisodeNumber != candidate.EpisodeNumber)
        {
            return false;
        }

        matchedValue = FormatNumber(identity.SeasonNumber!.Value, identity.EpisodeNumber!.Value);
        return true;
    }

    private static string FormatNumber(int seasonNumber, int episodeNumber)
    {
        return "S"
            + seasonNumber.ToString(CultureInfo.InvariantCulture)
            + "E"
            + episodeNumber.ToString(CultureInfo.InvariantCulture);
    }
}
