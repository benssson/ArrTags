using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.8 checks the fail-closed V1 location policy: local Movie and Episode
/// items remain eligible, while remote, virtual, offline, <c>.strm</c>,
/// fileless, and otherwise non-local items produce no badge and never proceed
/// into provider matching (ADR-008). Paths are never compared or used as
/// identity. These tests require no live Jellyfin or Arr instance.
/// </summary>
public class LocationEligibilityTests
{
    private static readonly Guid MovieItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SeriesItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EpisodeItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly ArrConnection RadarrConnection = BuildConnection(ArrProviderKind.Radarr);
    private static readonly ArrConnection SonarrConnection = BuildConnection(ArrProviderKind.Sonarr);

    [Fact]
    public void LocalFileMovieRemainsEligibleForMatching()
    {
        var identity = MovieIdentity(LocationType.FileSystem, "/media/movies/example.mkv", FileSource("/media/movies/example.mkv"));

        Assert.True(MediaLocationEligibility.IsEligible(identity));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { RadarrCandidate(5, 9, ("Tmdb", "603")) });

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(5, Assert.IsType<RadarrIdentity>(match.RecordIdentity).MovieId);
    }

    [Fact]
    public void LocalFileEpisodeRemainsEligibleForMatching()
    {
        var identity = EpisodeIdentity(
            LocationType.FileSystem,
            "/media/tv/example/s02e04.mkv",
            FileSource("/media/tv/example/s02e04.mkv"));

        Assert.True(MediaLocationEligibility.IsEligible(identity));

        var match = MediaMatcher.MatchEpisode(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            new[] { SonarrSeriesCandidate(10, ("Tvdb", "12345")) },
            new[] { SonarrEpisodeCandidate(10, 73, ("Tvdb", "9001")) });

        Assert.Equal(MediaMatchStatus.Matched, match.Status);
        Assert.Equal(MediaMatchMethod.ProviderId, match.MatchMethod);
        Assert.Equal(73, Assert.IsType<SonarrIdentity>(match.RecordIdentity).EpisodeId);
    }

    [Fact]
    public void RemoteMovieProducesNoBadge()
    {
        var identity = MovieIdentity(
            LocationType.Remote,
            "https://media.example/movie.mkv",
            RemoteSource("https://media.example/movie.mkv"));

        Assert.False(MediaLocationEligibility.IsEligible(identity));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { RadarrCandidate(5, 9, ("Tmdb", "603")) });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Null(match.RecordIdentity);
        Assert.Contains("remote", match.AmbiguityReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VirtualItemProducesNoBadge()
    {
        var identity = MovieIdentity(LocationType.Virtual, null, null);

        Assert.False(MediaLocationEligibility.IsEligible(identity));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { RadarrCandidate(5, 9, ("Tmdb", "603")) });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Null(match.RecordIdentity);
        Assert.Contains("virtual", match.AmbiguityReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StrmItemProducesNoBadge()
    {
        var identity = MovieIdentity(
            LocationType.FileSystem,
            "/media/movies/example.strm",
            RemoteSource("https://media.example/movie.mkv"));

        Assert.False(MediaLocationEligibility.IsEligible(identity));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { RadarrCandidate(5, 9, ("Tmdb", "603")) });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Null(match.RecordIdentity);
        Assert.Contains(".strm", match.AmbiguityReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfflineItemProducesNoBadge()
    {
        var identity = MovieIdentity(LocationType.Offline, "/media/movies/example.mkv", null);

        Assert.False(MediaLocationEligibility.IsEligible(identity));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { RadarrCandidate(5, 9, ("Tmdb", "603")) });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Null(match.RecordIdentity);
        Assert.Contains("offline", match.AmbiguityReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilelessLocalMovieProducesNoBadge()
    {
        var identity = MovieIdentity(LocationType.FileSystem, "/media/movies/example.mkv", null);

        Assert.False(MediaLocationEligibility.IsEligible(identity));

        var match = MediaMatcher.Match(
            identity,
            RadarrConnection.Provider,
            RadarrConnection.ConnectionId,
            new[] { RadarrCandidate(5, 9, ("Tmdb", "603")) });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Null(match.RecordIdentity);
        Assert.Contains("no eligible local file", match.AmbiguityReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteEpisodeDoesNotProceedToSeriesOrEpisodeMatching()
    {
        var identity = EpisodeIdentity(
            LocationType.Remote,
            "https://media.example/s02e04.mkv",
            RemoteSource("https://media.example/s02e04.mkv"));

        // Series and episode candidates would both resolve if matching were
        // allowed to proceed; the location gate must reject before any of it.
        var match = MediaMatcher.MatchEpisode(
            identity,
            SonarrConnection.Provider,
            SonarrConnection.ConnectionId,
            new[] { SonarrSeriesCandidate(10, ("Tvdb", "12345")) },
            new[] { SonarrEpisodeCandidate(10, 73, ("Tvdb", "9001")) });

        Assert.Equal(MediaMatchStatus.Unsupported, match.Status);
        Assert.Equal(MediaMatchMethod.None, match.MatchMethod);
        Assert.Null(match.RecordIdentity);
        Assert.Contains("remote", match.AmbiguityReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(LocationType.Remote, "remote")]
    [InlineData(LocationType.Virtual, "virtual")]
    [InlineData(LocationType.Offline, "offline")]
    public void IneligibleLocationKindsReportASafePathFreeReason(LocationType location, string expectedReasonFragment)
    {
        var identity = MovieIdentity(location, "/media/movies/example.mkv", null);

        Assert.True(MediaLocationEligibility.TryGetIneligibleReason(identity, out var reason));
        Assert.NotNull(reason);
        Assert.Contains(expectedReasonFragment, reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/media", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LocationCapturesRemoteAndStrmFactsFromJellyfin()
    {
        var remote = MovieIdentity(
            LocationType.Remote,
            "https://media.example/movie.mkv",
            RemoteSource("https://media.example/movie.mkv"));
        Assert.True(remote.MediaLocation!.IsRemote);

        var strm = MovieIdentity(
            LocationType.FileSystem,
            "/media/movies/example.strm",
            RemoteSource("https://media.example/movie.mkv"));
        Assert.True(strm.MediaLocation!.IsStrm);
    }

    [Fact]
    public void EligibilityRejectsIneligibleLocationEvenWhenScopeAndSurfaceAllowIt()
    {
        var configuration = PluginConfigurationSnapshot.From(new PluginConfiguration());
        var local = MovieIdentity(LocationType.FileSystem, "/media/movies/example.mkv", FileSource("/media/movies/example.mkv"));
        var remote = MovieIdentity(LocationType.Remote, "https://media.example/movie.mkv", RemoteSource("https://media.example/movie.mkv"));

        Assert.True(MediaEligibility.IsEligible(local, configuration));
        Assert.False(MediaEligibility.IsEligible(remote, configuration));
    }

    private static MediaIdentity MovieIdentity(
        LocationType location,
        string? path,
        MediaSourceInfo? source)
    {
        var resolver = new FakeMediaLibraryResolver();
        var movie = new TestMovie
        {
            Id = MovieItemId,
            Name = "Example Movie",
            Path = path,
            Location = location,
            Sources = source is null ? Array.Empty<MediaSourceInfo>() : new[] { source },
        };
        movie.ProviderIds["Tmdb"] = "603";

        Assert.True(MediaIdentityFactory.TryCreate(movie, resolver, out var identity));
        return identity!;
    }

    private static MediaIdentity EpisodeIdentity(
        LocationType location,
        string? path,
        MediaSourceInfo? source)
    {
        var resolver = new FakeMediaLibraryResolver();
        resolver.Items[SeriesItemId] = SeriesItem();
        var episode = new TestEpisode
        {
            Id = EpisodeItemId,
            Name = "Episode 4",
            SeriesId = SeriesItemId,
            ParentIndexNumber = 2,
            IndexNumber = 4,
            Path = path,
            Location = location,
            Sources = source is null ? Array.Empty<MediaSourceInfo>() : new[] { source },
        };
        episode.ProviderIds["Tvdb"] = "9001";

        Assert.True(MediaIdentityFactory.TryCreate(episode, resolver, out var identity));
        Assert.NotNull(identity!.SeriesIdentity);
        return identity;
    }

    private static TestSeries SeriesItem()
    {
        var series = new TestSeries { Id = SeriesItemId, Name = "Example Series" };
        series.ProviderIds["Tvdb"] = "12345";
        return series;
    }

    private static MediaSourceInfo FileSource(string path)
    {
        return new MediaSourceInfo
        {
            Id = "source-1",
            Path = path,
            Protocol = MediaProtocol.File,
        };
    }

    private static MediaSourceInfo RemoteSource(string path)
    {
        return new MediaSourceInfo
        {
            Id = "source-1",
            Path = path,
            Protocol = MediaProtocol.Http,
            IsRemote = true,
        };
    }

    private static MatchCandidate RadarrCandidate(int movieId, int fileId, params (string Key, string Value)[] providerIds)
    {
        return new MatchCandidate(
            RadarrConnection.ConnectionId,
            ArrProviderKind.Radarr,
            new RadarrIdentity(RadarrConnection.ConnectionId, movieId, ArrFileIdentity.Present(fileId)),
            ToDictionary(providerIds));
    }

    private static MatchCandidate SonarrSeriesCandidate(int seriesId, params (string Key, string Value)[] providerIds)
    {
        return new MatchCandidate(
            SonarrConnection.ConnectionId,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection.ConnectionId, seriesId),
            ToDictionary(providerIds));
    }

    private static MatchCandidate SonarrEpisodeCandidate(int seriesId, int episodeId, params (string Key, string Value)[] providerIds)
    {
        return new MatchCandidate(
            SonarrConnection.ConnectionId,
            ArrProviderKind.Sonarr,
            new SonarrIdentity(SonarrConnection.ConnectionId, seriesId, episodeId, ArrFileIdentity.Absent),
            ToDictionary(providerIds));
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

    private static ArrConnection BuildConnection(ArrProviderKind kind, string apiKey = "test-key")
    {
        var configuration = new PluginConfiguration();
        var connectionConfiguration = kind == ArrProviderKind.Sonarr ? configuration.Sonarr : configuration.Radarr;
        connectionConfiguration.Enabled = true;
        connectionConfiguration.BaseUrl = kind == ArrProviderKind.Sonarr
            ? "http://sonarr.local:8989"
            : "http://radarr.local:7878";
        connectionConfiguration.ApiKey = apiKey;

        return ArrConnectionCatalog.FromSnapshot(PluginConfigurationSnapshot.From(configuration))
            .Single(connection => connection.Provider.Kind == kind);
    }

    private sealed class FakeMediaLibraryResolver : IMediaLibraryResolver
    {
        public Dictionary<Guid, BaseItem> Items { get; } = new();

        public Guid? ResolveLibraryId(BaseItem item)
        {
            return null;
        }

        public BaseItem? ResolveItem(Guid itemId)
        {
            return Items.TryGetValue(itemId, out var item) ? item : null;
        }
    }

    private class TestMovie : Movie
    {
        public LocationType Location { get; set; } = LocationType.FileSystem;

        public IReadOnlyList<MediaSourceInfo> Sources { get; set; } = Array.Empty<MediaSourceInfo>();

        public override LocationType LocationType => Location;

        public override IReadOnlyList<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution) => Sources;
    }

    private class TestSeries : Series
    {
        public LocationType Location { get; set; } = LocationType.FileSystem;

        public override LocationType LocationType => Location;
    }

    private class TestEpisode : Episode
    {
        public LocationType Location { get; set; } = LocationType.FileSystem;

        public IReadOnlyList<MediaSourceInfo> Sources { get; set; } = Array.Empty<MediaSourceInfo>();

        public override LocationType LocationType => Location;

        public override IReadOnlyList<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution) => Sources;
    }
}
