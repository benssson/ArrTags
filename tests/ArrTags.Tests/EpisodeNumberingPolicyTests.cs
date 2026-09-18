using System;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.5 checks for the explicit V1 episode-numbering policy (DG-4). Number
/// fallback applies only to regular, single episodes and never to specials,
/// multi-episode spans, or absolute/scene numbering. These tests require no live
/// Jellyfin or Arr instance.
/// </summary>
public class EpisodeNumberingPolicyTests
{
    private static readonly Guid EpisodeItemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly ArrConnectionId SonarrConnection =
        ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:8989");
    private static readonly ArrConnectionId RadarrConnection =
        ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");

    [Fact]
    public void RegularEpisodeIdentityIsEligible()
    {
        Assert.True(EpisodeNumberingPolicy.IsEligible(EpisodeIdentity(2, 4)));
    }

    [Fact]
    public void RegularSonarrEpisodeCandidateIsEligible()
    {
        Assert.True(EpisodeNumberingPolicy.IsEligible(EpisodeCandidate(10, 20, 2, 4)));
    }

    [Fact]
    public void SeasonZeroSpecialIsNotEligibleOnEitherSide()
    {
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeIdentity(0, 4)));
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeCandidate(10, 20, 0, 4)));
    }

    [Fact]
    public void MultiEpisodeSpanIsNotEligibleOnEitherSide()
    {
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeIdentity(2, 4, episodeNumberEnd: 5)));
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeCandidate(10, 20, 2, 4, episodeNumberEnd: 5)));
    }

    [Fact]
    public void SpanEndingAtItsStartIsASingleEpisode()
    {
        Assert.True(EpisodeNumberingPolicy.IsEligible(EpisodeIdentity(2, 4, episodeNumberEnd: 4)));
    }

    [Fact]
    public void MissingNumberIsNotEligible()
    {
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeIdentity(season: null, episode: 4)));
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeIdentity(season: 2, episode: null)));
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeCandidate(10, 20, season: null, episode: 4)));
        Assert.False(EpisodeNumberingPolicy.IsEligible(EpisodeCandidate(10, 20, season: 2, episode: null)));
    }

    [Fact]
    public void NonEpisodeIdentityIsNotEligible()
    {
        var series = new MediaIdentity(
            EpisodeItemId,
            MediaItemType.Series,
            seasonNumber: 2,
            episodeNumber: 4);

        Assert.False(EpisodeNumberingPolicy.IsEligible(series));
    }

    [Fact]
    public void NonSonarrCandidateIsNotEligible()
    {
        var candidate = new MatchCandidate(
            RadarrConnection,
            ArrProviderKind.Radarr,
            new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Absent),
            seasonNumber: 2,
            episodeNumber: 4);

        Assert.False(EpisodeNumberingPolicy.IsEligible(candidate));
    }

    [Fact]
    public void HasMultiEpisodeSpanRequiresAnEndGreaterThanTheStart()
    {
        Assert.True(EpisodeNumberingPolicy.HasMultiEpisodeSpan(4, 6));
        Assert.False(EpisodeNumberingPolicy.HasMultiEpisodeSpan(4, 4));
        Assert.False(EpisodeNumberingPolicy.HasMultiEpisodeSpan(4, null));
        Assert.False(EpisodeNumberingPolicy.HasMultiEpisodeSpan(null, 6));
    }

    [Fact]
    public void TryMatchAcceptsExactSeasonAndEpisodeNumbers()
    {
        var matched = EpisodeNumberingPolicy.TryMatch(
            EpisodeIdentity(2, 4),
            EpisodeCandidate(10, 20, 2, 4),
            out var matchedValue);

        Assert.True(matched);
        Assert.Equal("S2E4", matchedValue);
    }

    [Fact]
    public void TryMatchRejectsMismatchedSeasonOrEpisode()
    {
        Assert.False(EpisodeNumberingPolicy.TryMatch(
            EpisodeIdentity(2, 4),
            EpisodeCandidate(10, 20, 3, 4),
            out var seasonMismatch));
        Assert.Null(seasonMismatch);

        Assert.False(EpisodeNumberingPolicy.TryMatch(
            EpisodeIdentity(2, 4),
            EpisodeCandidate(10, 20, 2, 5),
            out var episodeMismatch));
        Assert.Null(episodeMismatch);
    }

    [Fact]
    public void TryMatchRejectsSpecialsAndSpans()
    {
        Assert.False(EpisodeNumberingPolicy.TryMatch(
            EpisodeIdentity(0, 4),
            EpisodeCandidate(10, 20, 0, 4),
            out _));

        Assert.False(EpisodeNumberingPolicy.TryMatch(
            EpisodeIdentity(2, 4, episodeNumberEnd: 5),
            EpisodeCandidate(10, 20, 2, 4),
            out _));
    }

    [Fact]
    public void PolicyRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => EpisodeNumberingPolicy.IsEligible((MediaIdentity)null!));
        Assert.Throws<ArgumentNullException>(() => EpisodeNumberingPolicy.IsEligible((MatchCandidate)null!));
        Assert.Throws<ArgumentNullException>(
            () => EpisodeNumberingPolicy.TryMatch(null!, EpisodeCandidate(10, 20, 2, 4), out _));
        Assert.Throws<ArgumentNullException>(
            () => EpisodeNumberingPolicy.TryMatch(EpisodeIdentity(2, 4), null!, out _));
    }

    private static MediaIdentity EpisodeIdentity(int? season, int? episode, int? episodeNumberEnd = null)
    {
        return new MediaIdentity(
            EpisodeItemId,
            MediaItemType.Episode,
            seasonNumber: season,
            episodeNumber: episode,
            episodeNumberEnd: episodeNumberEnd);
    }

    private static MatchCandidate EpisodeCandidate(
        int seriesId,
        int episodeId,
        int? season,
        int? episode,
        int? episodeNumberEnd = null)
    {
        return new MatchCandidate(
            SonarrConnection,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection, seriesId, episodeId, ArrFileIdentity.Absent),
            seasonNumber: season,
            episodeNumber: episode,
            episodeNumberEnd: episodeNumberEnd);
    }
}
