using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Matching;
using ArrTags.Metadata;
using ArrTags.Providers;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.7 checks that every Arr-local record and file identifier is scoped by
/// its originating <see cref="ArrConnection"/>, so an identical numeric ID on
/// two Sonarr or Radarr instances is never the same identity. V1 configures at
/// most one connection per provider kind, but the model still permits distinct
/// connection scopes; these tests build two scopes per provider and prove the
/// identities, candidates, matches, and metadata fingerprints cannot collide.
/// These tests require no live Jellyfin or Arr instance.
/// </summary>
public class ConnectionScopingTests
{
    private static readonly Guid MovieItemId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid SeriesItemId = Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid EpisodeItemId = Guid.Parse("33333333-4444-5555-6666-777777777777");
    private static readonly DateTimeOffset ObservedAt =
        new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly ArrConnectionId RadarrConnectionA =
        ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr-a.local:7878");
    private static readonly ArrConnectionId RadarrConnectionB =
        ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr-b.local:7878");
    private static readonly ArrConnectionId SonarrConnectionA =
        ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr-a.local:8989");
    private static readonly ArrConnectionId SonarrConnectionB =
        ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr-b.local:8989");

    [Fact]
    public void RadarrIdentitiesWithIdenticalLocalIdsOnDifferentConnectionsAreDistinct()
    {
        var first = new RadarrIdentity(RadarrConnectionA, 7, ArrFileIdentity.Present(42));
        var second = new RadarrIdentity(RadarrConnectionB, 7, ArrFileIdentity.Present(42));

        Assert.NotEqual<ArrRecordIdentity>(first, second);
        Assert.Equal(RadarrConnectionA, first.ConnectionId);
        Assert.Equal(RadarrConnectionB, second.ConnectionId);
        Assert.Contains(RadarrConnectionA.Value, first.ToString(), StringComparison.Ordinal);
        Assert.Contains(RadarrConnectionB.Value, second.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SonarrIdentitiesWithIdenticalLocalIdsOnDifferentConnectionsAreDistinct()
    {
        var first = new SonarrIdentity(SonarrConnectionA, 12, 73, ArrFileIdentity.Present(418));
        var second = new SonarrIdentity(SonarrConnectionB, 12, 73, ArrFileIdentity.Present(418));

        Assert.NotEqual<ArrRecordIdentity>(first, second);
        Assert.Equal(SonarrConnectionA, first.ConnectionId);
        Assert.Equal(SonarrConnectionB, second.ConnectionId);
        Assert.NotEqual(first.ToString(), second.ToString());
    }

    [Fact]
    public void IdenticalLocalIdsAcrossProviderKindsOnTheSameHostAreDistinct()
    {
        var radarrId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://media.local:8080");
        var sonarrId = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://media.local:8080");

        var radarr = new RadarrIdentity(radarrId, 7, ArrFileIdentity.Present(42));
        var sonarr = new SonarrIdentity(sonarrId, 7, 42, ArrFileIdentity.Present(42));

        Assert.NotEqual(radarrId, sonarrId);
        Assert.NotEqual<ArrRecordIdentity>(radarr, sonarr);
    }

    [Fact]
    public void FileIdentityIsBoundedByItsScopedRecordIdentity()
    {
        // An ArrFileIdentity has no connection of its own; two file IDs are equal
        // in isolation. Scoping is supplied by the enclosing record identity, so
        // the same file ID on two connections is still two distinct identities.
        Assert.Equal(ArrFileIdentity.Present(42), ArrFileIdentity.Present(42));

        var first = new RadarrIdentity(RadarrConnectionA, 7, ArrFileIdentity.Present(42));
        var second = new RadarrIdentity(RadarrConnectionB, 7, ArrFileIdentity.Present(42));

        Assert.Equal(first.MovieFileIdentity, second.MovieFileIdentity);
        Assert.NotEqual<ArrRecordIdentity>(first, second);
    }

    [Fact]
    public void MatchCandidateRejectsRadarrIdentityFromAnotherConnection()
    {
        var identity = new RadarrIdentity(RadarrConnectionB, 7, ArrFileIdentity.Present(42));

        var exception = Assert.Throws<ArgumentException>(() => new MatchCandidate(
            RadarrConnectionA,
            ArrProviderKind.Radarr,
            identity));

        Assert.Equal("recordIdentity", exception.ParamName);
    }

    [Fact]
    public void MatchCandidateRejectsSonarrIdentityFromAnotherConnection()
    {
        var identity = new SonarrIdentity(SonarrConnectionB, 12, 73, ArrFileIdentity.Present(418));

        var exception = Assert.Throws<ArgumentException>(() => new MatchCandidate(
            SonarrConnectionA,
            ArrProviderKind.Sonarr,
            identity));

        Assert.Equal("recordIdentity", exception.ParamName);
    }

    [Fact]
    public void MediaMatchRejectsRadarrIdentityFromAnotherConnection()
    {
        var identity = new RadarrIdentity(RadarrConnectionB, 7, ArrFileIdentity.Present(42));

        Assert.Throws<ArgumentException>(() => new MediaMatch(
            MovieIdentity(),
            RadarrProvider(RadarrConnectionA),
            RadarrConnectionA,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            identity));
    }

    [Fact]
    public void MediaMatchRejectsSonarrIdentityFromAnotherConnection()
    {
        var identity = new SonarrIdentity(SonarrConnectionB, 12, 73, ArrFileIdentity.Present(418));

        Assert.Throws<ArgumentException>(() => new MediaMatch(
            SeriesIdentity(),
            SonarrProvider(SonarrConnectionA),
            SonarrConnectionA,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            identity));
    }

    [Fact]
    public void MediaMatchFingerprintDistinguishesIdenticalLocalIdsOnDifferentConnections()
    {
        var first = RadarrMatch(RadarrConnectionA);
        var second = RadarrMatch(RadarrConnectionB);

        Assert.Equal(7, Assert.IsType<RadarrIdentity>(first.RecordIdentity).MovieId);
        Assert.Equal(RadarrConnectionA, first.RecordIdentity!.ConnectionId);
        Assert.Equal(RadarrConnectionB, second.RecordIdentity!.ConnectionId);
        Assert.NotEqual(first.MatchFingerprint, second.MatchFingerprint);
    }

    [Fact]
    public void BadgeMetadataFingerprintDistinguishesIdenticalLocalIdsOnDifferentConnections()
    {
        var first = new BadgeMetadata(
            RadarrProvider(RadarrConnectionA),
            new RadarrIdentity(RadarrConnectionA, 7, ArrFileIdentity.Present(42)),
            ObservedAt);
        var second = new BadgeMetadata(
            RadarrProvider(RadarrConnectionB),
            new RadarrIdentity(RadarrConnectionB, 7, ArrFileIdentity.Present(42)),
            ObservedAt);

        Assert.NotEqual(first.MetadataFingerprint, second.MetadataFingerprint);
    }

    [Fact]
    public void SonarrMetadataFingerprintDistinguishesIdenticalLocalIdsOnDifferentConnections()
    {
        var first = new BadgeMetadata(
            SonarrProvider(SonarrConnectionA),
            new SonarrIdentity(SonarrConnectionA, 12, 73, ArrFileIdentity.Present(418)),
            ObservedAt);
        var second = new BadgeMetadata(
            SonarrProvider(SonarrConnectionB),
            new SonarrIdentity(SonarrConnectionB, 12, 73, ArrFileIdentity.Present(418)),
            ObservedAt);

        Assert.NotEqual(first.MetadataFingerprint, second.MetadataFingerprint);
    }

    [Fact]
    public void CatalogDerivesDistinctConnectionScopesFromDifferentBaseUrls()
    {
        var first = RadarrConnectionFrom("http://radarr-a.local:7878");
        var second = RadarrConnectionFrom("http://radarr-b.local:7878");

        Assert.NotEqual(first.ConnectionId, second.ConnectionId);
        Assert.Equal(first.ConnectionId.Value, first.Provider.ProviderInstanceId);
        Assert.Equal(second.ConnectionId.Value, second.Provider.ProviderInstanceId);
    }

    [Fact]
    public void MatcherResolvesIdenticalRadarrLocalIdsToTheRequestedConnection()
    {
        var identity = MovieIdentity(("Tmdb", "603"));
        var candidateA = new MatchCandidate(
            RadarrConnectionA,
            ArrProviderKind.Radarr,
            new RadarrIdentity(RadarrConnectionA, 7, ArrFileIdentity.Present(42)),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" });
        var candidateB = new MatchCandidate(
            RadarrConnectionB,
            ArrProviderKind.Radarr,
            new RadarrIdentity(RadarrConnectionB, 7, ArrFileIdentity.Present(42)),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" });

        var matchA = MediaMatcher.Match(
            identity, RadarrProvider(RadarrConnectionA), RadarrConnectionA, new[] { candidateA });
        var matchB = MediaMatcher.Match(
            identity, RadarrProvider(RadarrConnectionB), RadarrConnectionB, new[] { candidateB });

        Assert.Equal(MediaMatchStatus.Matched, matchA.Status);
        Assert.Equal(MediaMatchStatus.Matched, matchB.Status);
        Assert.Equal(RadarrConnectionA, matchA.RecordIdentity!.ConnectionId);
        Assert.Equal(RadarrConnectionB, matchB.RecordIdentity!.ConnectionId);
        Assert.NotEqual(matchA.MatchFingerprint, matchB.MatchFingerprint);
    }

    [Fact]
    public void MatcherResolvesIdenticalSonarrLocalIdsToTheRequestedConnection()
    {
        var seriesIdentity = SeriesIdentity(("Tvdb", "12345"));
        var episodeIdentity = EpisodeIdentity(seriesIdentity, ("Tvdb", "9001"));
        var seriesA = new MatchCandidate(
            SonarrConnectionA,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnectionA, 12),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "12345" });
        var seriesB = new MatchCandidate(
            SonarrConnectionB,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnectionB, 12),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "12345" });
        var episodeA = new MatchCandidate(
            SonarrConnectionA,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnectionA, 12, 73, ArrFileIdentity.Present(418)),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "9001" });
        var episodeB = new MatchCandidate(
            SonarrConnectionB,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnectionB, 12, 73, ArrFileIdentity.Present(418)),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "9001" });

        var matchA = MediaMatcher.MatchEpisode(
            episodeIdentity, SonarrProvider(SonarrConnectionA), SonarrConnectionA, new[] { seriesA }, new[] { episodeA });
        var matchB = MediaMatcher.MatchEpisode(
            episodeIdentity, SonarrProvider(SonarrConnectionB), SonarrConnectionB, new[] { seriesB }, new[] { episodeB });

        Assert.Equal(MediaMatchStatus.Matched, matchA.Status);
        Assert.Equal(MediaMatchStatus.Matched, matchB.Status);
        Assert.Equal(SonarrConnectionA, matchA.RecordIdentity!.ConnectionId);
        Assert.Equal(SonarrConnectionB, matchB.RecordIdentity!.ConnectionId);
        Assert.Equal(73, Assert.IsType<SonarrIdentity>(matchA.RecordIdentity).EpisodeId);
        Assert.Equal(73, Assert.IsType<SonarrIdentity>(matchB.RecordIdentity).EpisodeId);
        Assert.NotEqual(matchA.MatchFingerprint, matchB.MatchFingerprint);
    }

    private static MediaMatch RadarrMatch(ArrConnectionId connection)
    {
        return new MediaMatch(
            MovieIdentity(("Tmdb", "603")),
            RadarrProvider(connection),
            connection,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            new RadarrIdentity(connection, 7, ArrFileIdentity.Present(42)));
    }

    private static MediaIdentity MovieIdentity(params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(MovieItemId, MediaItemType.Movie, providerIds: ToDictionary(providerIds));
    }

    private static MediaIdentity SeriesIdentity(params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(SeriesItemId, MediaItemType.Series, providerIds: ToDictionary(providerIds));
    }

    private static MediaIdentity EpisodeIdentity(
        MediaIdentity seriesIdentity,
        params (string Key, string Value)[] providerIds)
    {
        return new MediaIdentity(
            EpisodeItemId,
            MediaItemType.Episode,
            providerIds: ToDictionary(providerIds),
            seriesIdentity: seriesIdentity);
    }

    private static ArrProvider RadarrProvider(ArrConnectionId connection)
    {
        return new ArrProvider(ArrProviderKind.Radarr, connection.Value, "Radarr");
    }

    private static ArrProvider SonarrProvider(ArrConnectionId connection)
    {
        return new ArrProvider(ArrProviderKind.Sonarr, connection.Value, "Sonarr");
    }

    private static ArrConnection RadarrConnectionFrom(string baseUrl)
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = baseUrl;
        configuration.Radarr.ApiKey = "test-key";

        return ArrConnectionCatalog.FromSnapshot(PluginConfigurationSnapshot.From(configuration))
            .Single(connection => connection.Provider.Kind == ArrProviderKind.Radarr);
    }

    private static Dictionary<string, string> ToDictionary((string Key, string Value)[] providerIds)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in providerIds)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }
}
