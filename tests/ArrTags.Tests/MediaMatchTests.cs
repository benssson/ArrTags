using System;
using System.Collections.Generic;
using ArrTags.Media;
using ArrTags.Matching;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.2 checks for the canonical <see cref="MediaMatch"/> model and its
/// deterministic fingerprint. These tests require no live Jellyfin or Arr
/// instance.
/// </summary>
public class MediaMatchTests
{
    private static readonly Guid ItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly ArrConnectionId RadarrConnection =
        ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
    private static readonly ArrConnectionId OtherRadarrConnection =
        ArrConnectionId.For(ArrProviderKind.Radarr, "http://other-radarr.local:7878");
    private static readonly ArrProvider RadarrProvider =
        new(ArrProviderKind.Radarr, RadarrConnection.Value, "Radarr");

    [Fact]
    public void FingerprintIsDeterministicAndExcludesMatchedAt()
    {
        var mediaIdentity = MovieIdentity();
        var recordIdentity = new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Present(9));

        var first = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            recordIdentity,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" },
            matchedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var second = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            recordIdentity,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tmdb"] = "603" },
            matchedAt: new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(first.MatchFingerprint, second.MatchFingerprint);
        Assert.Equal(64, first.MatchFingerprint.Length);
    }

    [Fact]
    public void FingerprintChangesWhenMatchEvidenceChanges()
    {
        var mediaIdentity = MovieIdentity();
        var recordIdentity = new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Present(9));

        var byTmdb = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            recordIdentity,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" });
        var byImdb = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            recordIdentity,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Imdb"] = "tt0133093" });

        Assert.NotEqual(byTmdb.MatchFingerprint, byImdb.MatchFingerprint);
    }

    [Fact]
    public void FingerprintChangesWhenFilePresenceChanges()
    {
        var mediaIdentity = MovieIdentity();

        var present = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Present(9)));
        var absent = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Absent));

        Assert.NotEqual(present.MatchFingerprint, absent.MatchFingerprint);
    }

    [Fact]
    public void FingerprintChangesWhenConnectionScopeChanges()
    {
        var mediaIdentity = MovieIdentity();
        var otherProvider = new ArrProvider(ArrProviderKind.Radarr, OtherRadarrConnection.Value, "Radarr");

        var first = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Present(9)));
        var second = new MediaMatch(
            mediaIdentity,
            otherProvider,
            OtherRadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new RadarrIdentity(OtherRadarrConnection, 5, ArrFileIdentity.Present(9)));

        Assert.NotEqual(first.MatchFingerprint, second.MatchFingerprint);
    }

    [Fact]
    public void FingerprintChangesWhenMatchMethodChanges()
    {
        var mediaIdentity = MovieIdentity();
        var recordIdentity = new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Present(9));

        var automatic = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            recordIdentity);
        var manual = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.Manual,
            recordIdentity);

        Assert.NotEqual(automatic.MatchFingerprint, manual.MatchFingerprint);
    }

    [Fact]
    public void MatchedStatusRequiresRecordIdentityAndConcreteMethod()
    {
        var mediaIdentity = MovieIdentity();

        var missingIdentity = Assert.Throws<ArgumentException>(() => new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId));
        Assert.Equal("recordIdentity", missingIdentity.ParamName);

        var missingMethod = Assert.Throws<ArgumentException>(() => new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.None,
            new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Absent)));
        Assert.Equal("matchMethod", missingMethod.ParamName);
    }

    [Fact]
    public void NonMatchedStatusCannotCarryRecordIdentity()
    {
        var mediaIdentity = MovieIdentity();

        Assert.Throws<ArgumentException>(() => new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.NotFound,
            MediaMatchMethod.None,
            new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Absent)));
    }

    [Fact]
    public void MatchedStatusRejectsRecordIdentityFromAnotherConnection()
    {
        var mediaIdentity = MovieIdentity();

        Assert.Throws<ArgumentException>(() => new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new RadarrIdentity(OtherRadarrConnection, 5, ArrFileIdentity.Absent)));
    }

    [Fact]
    public void MatchedStatusRejectsRecordIdentityForDifferentProviderKind()
    {
        var mediaIdentity = MovieIdentity();
        var sonarrConnection = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.local:8989");

        Assert.Throws<ArgumentException>(() => new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            sonarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new SonarrIdentity(sonarrConnection, 5)));
    }

    [Fact]
    public void MatchedProviderIdsAreNormalized()
    {
        var mediaIdentity = MovieIdentity();
        var match = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Present(9)),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [" tmdb "] = " 603 ",
                ["blank"] = "   ",
            });

        Assert.Equal("603", match.MatchedProviderIds["Tmdb"]);
        Assert.False(match.MatchedProviderIds.ContainsKey("blank"));
    }

    [Fact]
    public void StaleStatusMayRetainTheLastValidatedIdentity()
    {
        var mediaIdentity = MovieIdentity();
        var recordIdentity = new RadarrIdentity(RadarrConnection, 5, ArrFileIdentity.Present(9));

        var match = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Stale,
            MediaMatchMethod.ProviderId,
            recordIdentity);

        Assert.Equal(MediaMatchStatus.Stale, match.Status);
        Assert.Same(recordIdentity, match.RecordIdentity);
    }

    [Fact]
    public void AmbiguousStatusCarriesSafeReasonAndNoIdentity()
    {
        var mediaIdentity = MovieIdentity();

        var match = new MediaMatch(
            mediaIdentity,
            RadarrProvider,
            RadarrConnection,
            MediaMatchStatus.Ambiguous,
            MediaMatchMethod.None,
            ambiguityReason: "  Multiple Radarr movies shared the TMDb identifier.  ");

        Assert.Null(match.RecordIdentity);
        Assert.Equal("Multiple Radarr movies shared the TMDb identifier.", match.AmbiguityReason);
    }

    private static MediaIdentity MovieIdentity()
    {
        return new MediaIdentity(
            ItemId,
            MediaItemType.Movie,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tmdb"] = "603",
            });
    }
}
