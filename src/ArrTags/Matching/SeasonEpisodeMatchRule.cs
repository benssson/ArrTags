using System;
using ArrTags.Media;

namespace ArrTags.Matching;

/// <summary>
/// A candidate rule that matches a Jellyfin episode to a Sonarr episode by exact
/// season and episode number after the parent series has matched. It delegates
/// to <see cref="EpisodeNumberingPolicy"/>, so specials, multi-episode spans,
/// and absolute/scene numbering are never matched by number.
/// </summary>
public sealed class SeasonEpisodeMatchRule : CandidateMatchRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SeasonEpisodeMatchRule"/> class.
    /// </summary>
    public SeasonEpisodeMatchRule()
        : base(MediaMatchMethod.Number, EpisodeNumberingPolicy.NumberEvidenceKey)
    {
    }

    /// <inheritdoc />
    public override bool IsSatisfiedBy(MediaIdentity identity, MatchCandidate candidate, out string? matchedValue)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(candidate);

        return EpisodeNumberingPolicy.TryMatch(identity, candidate, out matchedValue);
    }
}
