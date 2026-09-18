using System;
using System.Collections.Generic;
using System.Reflection;
using ArrTags.Configuration;
using ArrTags.Media;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 3.1 checks for building canonical <see cref="MediaIdentity"/> snapshots
/// from supported Jellyfin item types and applying library scope and the V1
/// badge-surface distinction. These tests require no live Jellyfin or Arr
/// instance.
/// </summary>
public class MediaIdentityTests
{
    private static readonly Guid ItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherItemId = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid LibraryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherLibraryId = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid SeriesId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void MovieSnapshotCapturesIdentityContextAndLocation()
    {
        var resolver = new FakeMediaLibraryResolver();
        resolver.LibraryIds[ItemId] = LibraryId;
        var movie = new TestMovie
        {
            Id = ItemId,
            Name = "Example Movie",
            ProductionYear = 2021,
            Path = "/media/movies/example.mkv",
            Location = LocationType.FileSystem,
            Sources = new[] { Source("source-1", "/media/movies/example.mkv", MediaProtocol.File, 1024, "etag-1") },
        };
        movie.ProviderIds["Tmdb"] = "603";
        movie.ProviderIds["Imdb"] = "tt0133093";

        Assert.True(MediaIdentityFactory.TryCreate(movie, resolver, out var identity));

        Assert.NotNull(identity);
        Assert.Equal(ItemId, identity!.JellyfinItemId);
        Assert.Equal(MediaItemType.Movie, identity.ItemType);
        Assert.Equal(LibraryId, identity.LibraryId);
        Assert.Equal("Example Movie", identity.Title);
        Assert.Equal(2021, identity.ProductionYear);
        Assert.Equal("603", identity.ProviderIds["Tmdb"]);
        Assert.Equal("tt0133093", identity.ProviderIds["imdb"]);
        Assert.Null(identity.SeriesIdentity);
        Assert.Null(identity.SeasonNumber);
        Assert.Null(identity.EpisodeNumber);
        Assert.Null(identity.EpisodeNumberEnd);
        Assert.NotNull(identity.MediaLocation);
        Assert.Equal(MediaLocationKind.FileSystem, identity.MediaLocation!.Kind);
        Assert.True(identity.MediaLocation.IsFileProtocol);
        Assert.Equal(1, identity.MediaLocation.MediaSourceCount);
        Assert.Equal("/media/movies/example.mkv", identity.MediaLocation.PrimaryPath);
        Assert.False(string.IsNullOrEmpty(identity.SourceFingerprint));
    }

    [Fact]
    public void SeriesSnapshotIsStructuralAndHasNoNumbering()
    {
        var resolver = new FakeMediaLibraryResolver();
        resolver.LibraryIds[ItemId] = LibraryId;
        var series = new TestSeries
        {
            Id = ItemId,
            Name = "Example Series",
            Path = "/media/tv/example",
        };
        series.ProviderIds["Tvdb"] = "12345";

        Assert.True(MediaIdentityFactory.TryCreate(series, resolver, out var identity));

        Assert.NotNull(identity);
        Assert.Equal(MediaItemType.Series, identity!.ItemType);
        Assert.Null(identity.SeriesIdentity);
        Assert.Null(identity.SeasonNumber);
        Assert.Null(identity.EpisodeNumber);
        Assert.Null(identity.EpisodeNumberEnd);
        Assert.Equal(0, identity.MediaLocation!.MediaSourceCount);
        Assert.Equal("12345", identity.ProviderIds["Tvdb"]);
    }

    [Fact]
    public void SeasonSnapshotCapturesParentSeriesContextAndSeasonNumber()
    {
        var resolver = new FakeMediaLibraryResolver();
        resolver.LibraryIds[ItemId] = LibraryId;
        resolver.LibraryIds[SeriesId] = LibraryId;
        resolver.Items[SeriesId] = SeriesItem();
        var season = new TestSeason
        {
            Id = ItemId,
            Name = "Season 2",
            IndexNumber = 2,
            SeriesId = SeriesId,
        };

        Assert.True(MediaIdentityFactory.TryCreate(season, resolver, out var identity));

        Assert.NotNull(identity);
        Assert.Equal(MediaItemType.Season, identity!.ItemType);
        Assert.Equal(2, identity.SeasonNumber);
        Assert.Null(identity.EpisodeNumber);
        Assert.Null(identity.EpisodeNumberEnd);
        Assert.NotNull(identity.SeriesIdentity);
        Assert.Equal(MediaItemType.Series, identity.SeriesIdentity!.ItemType);
        Assert.Equal(SeriesId, identity.SeriesIdentity.JellyfinItemId);
        Assert.Equal(LibraryId, identity.SeriesIdentity.LibraryId);
        Assert.Equal("12345", identity.SeriesIdentity.ProviderIds["Tvdb"]);
    }

    [Fact]
    public void EpisodeSnapshotCapturesRawNumberingAndSeriesContext()
    {
        var resolver = new FakeMediaLibraryResolver();
        resolver.LibraryIds[ItemId] = LibraryId;
        resolver.Items[SeriesId] = SeriesItem();
        var episode = new TestEpisode
        {
            Id = ItemId,
            Name = "Episode 4",
            SeriesId = SeriesId,
            ParentIndexNumber = 2,
            IndexNumber = 4,
            IndexNumberEnd = 5,
            Location = LocationType.FileSystem,
            Sources = new[] { Source("source-1", "/media/tv/example/s02e04.mkv", MediaProtocol.File) },
        };
        episode.ProviderIds["Tvdb"] = "999";

        Assert.True(MediaIdentityFactory.TryCreate(episode, resolver, out var identity));

        Assert.NotNull(identity);
        Assert.Equal(MediaItemType.Episode, identity!.ItemType);
        Assert.Equal(2, identity.SeasonNumber);
        Assert.Equal(4, identity.EpisodeNumber);
        Assert.Equal(5, identity.EpisodeNumberEnd);
        Assert.NotNull(identity.SeriesIdentity);
        Assert.Equal(SeriesId, identity.SeriesIdentity!.JellyfinItemId);
        Assert.Equal("999", identity.ProviderIds["Tvdb"]);
        Assert.Equal("/media/tv/example/s02e04.mkv", identity.MediaLocation!.PrimaryPath);
    }

    [Fact]
    public void SeasonAndEpisodeTolerateUnresolvableParentSeries()
    {
        var resolver = new FakeMediaLibraryResolver();
        var episode = new TestEpisode
        {
            Id = ItemId,
            Name = "Orphan",
            SeriesId = SeriesId,
            ParentIndexNumber = 1,
            IndexNumber = 1,
        };

        Assert.True(MediaIdentityFactory.TryCreate(episode, resolver, out var identity));
        Assert.NotNull(identity);
        Assert.Null(identity!.SeriesIdentity);
    }

    [Fact]
    public void UnsupportedItemTypeIsRejected()
    {
        var resolver = new FakeMediaLibraryResolver();
        var video = new Video { Id = ItemId, Name = "Generic video" };

        Assert.False(MediaIdentityFactory.TryCreate(video, resolver, out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void EmptyItemIdentifierIsRejected()
    {
        var resolver = new FakeMediaLibraryResolver();
        var movie = new TestMovie { Name = "No id" };

        Assert.False(MediaIdentityFactory.TryCreate(movie, resolver, out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void LocationSummaryCapturesRemoteItemsAndMultipleSources()
    {
        var resolver = new FakeMediaLibraryResolver();
        var movie = new TestMovie
        {
            Id = ItemId,
            Name = "Remote",
            Location = LocationType.Remote,
            Sources = new[]
            {
                Source("source-1", "https://media.example/one.mkv", MediaProtocol.Http),
                Source("source-2", "https://media.example/two.mkv", MediaProtocol.Http),
            },
        };

        Assert.True(MediaIdentityFactory.TryCreate(movie, resolver, out var identity));

        Assert.Equal(MediaLocationKind.Remote, identity!.MediaLocation!.Kind);
        Assert.False(identity.MediaLocation.IsFileProtocol);
        Assert.Equal(2, identity.MediaLocation.MediaSourceCount);
        Assert.Equal("https://media.example/one.mkv", identity.MediaLocation.PrimaryPath);
    }

    [Fact]
    public void ProviderIdentifiersDropBlankValues()
    {
        var resolver = new FakeMediaLibraryResolver();
        var movie = new TestMovie { Id = ItemId, Name = "Example" };
        movie.ProviderIds["Tmdb"] = "603";
        movie.ProviderIds["Imdb"] = " ";

        Assert.True(MediaIdentityFactory.TryCreate(movie, resolver, out var identity));

        Assert.True(identity!.ProviderIds.ContainsKey("Tmdb"));
        Assert.False(identity.ProviderIds.ContainsKey("Imdb"));
    }

    [Fact]
    public void SourceFingerprintIsStableAndSourceSensitive()
    {
        var resolver = new FakeMediaLibraryResolver();
        var movie = new TestMovie
        {
            Id = ItemId,
            Name = "Example",
            Sources = new[] { Source("source-1", "/media/movies/example.mkv", MediaProtocol.File, 1024, "etag-1") },
        };

        Assert.True(MediaIdentityFactory.TryCreate(movie, resolver, out var first));
        Assert.True(MediaIdentityFactory.TryCreate(movie, resolver, out var second));
        Assert.Equal(first!.SourceFingerprint, second!.SourceFingerprint);

        movie.Sources = new[] { Source("source-1", "/media/movies/example.mkv", MediaProtocol.File, 2048, "etag-2") };
        Assert.True(MediaIdentityFactory.TryCreate(movie, resolver, out var changed));
        Assert.NotEqual(first.SourceFingerprint, changed!.SourceFingerprint);
    }

    [Fact]
    public void EmptyLibraryScopeMeansNoRestriction()
    {
        var configuration = BuildConfiguration();

        Assert.True(MediaEligibility.IsInLibraryScope(Identity(MediaItemType.Movie, LibraryId), configuration));
        Assert.True(MediaEligibility.IsInLibraryScope(Identity(MediaItemType.Movie, null), configuration));
    }

    [Fact]
    public void LibraryScopeMatchesCollectionFolderIdentifier()
    {
        var configuration = BuildConfiguration(new[] { LibraryId.ToString() });

        Assert.True(MediaEligibility.IsInLibraryScope(Identity(MediaItemType.Movie, LibraryId), configuration));
        Assert.False(MediaEligibility.IsInLibraryScope(Identity(MediaItemType.Movie, OtherLibraryId), configuration));
    }

    [Fact]
    public void LibraryScopeFailsClosedWhenLibraryIsUnknown()
    {
        var configuration = BuildConfiguration(new[] { LibraryId.ToString() });

        Assert.False(MediaEligibility.IsInLibraryScope(Identity(MediaItemType.Movie, null), configuration));
    }

    [Fact]
    public void LibraryScopeNeverMatchesDisplayNames()
    {
        var configuration = BuildConfiguration(new[] { "Movies" });

        Assert.False(MediaEligibility.IsInLibraryScope(Identity(MediaItemType.Movie, LibraryId), configuration));
    }

    [Fact]
    public void BadgeSurfaceIsLimitedToMovieAndEpisode()
    {
        var configuration = BuildConfiguration();

        Assert.True(MediaEligibility.IsBadgeSurface(Identity(MediaItemType.Movie, LibraryId), configuration));
        Assert.True(MediaEligibility.IsBadgeSurface(Identity(MediaItemType.Episode, LibraryId), configuration));
        Assert.False(MediaEligibility.IsBadgeSurface(Identity(MediaItemType.Series, LibraryId), configuration));
        Assert.False(MediaEligibility.IsBadgeSurface(Identity(MediaItemType.Season, LibraryId), configuration));
    }

    [Fact]
    public void BadgeSurfaceHonoursConfiguredPosterFlags()
    {
        var configuration = BuildConfiguration(badgeMoviePosters: false, badgeEpisodePosters: false);

        Assert.False(MediaEligibility.IsBadgeSurface(Identity(MediaItemType.Movie, LibraryId), configuration));
        Assert.False(MediaEligibility.IsBadgeSurface(Identity(MediaItemType.Episode, LibraryId), configuration));
    }

    [Fact]
    public void EligibilityCombinesLibraryScopeAndBadgeSurface()
    {
        var configuration = BuildConfiguration(new[] { LibraryId.ToString() });

        Assert.True(MediaEligibility.IsEligible(Identity(MediaItemType.Movie, LibraryId), configuration));
        Assert.False(MediaEligibility.IsEligible(Identity(MediaItemType.Movie, OtherLibraryId), configuration));
        Assert.False(MediaEligibility.IsEligible(Identity(MediaItemType.Series, LibraryId), configuration));
    }

    [Fact]
    public void CanonicalMediaIdentityDoesNotExposeProviderDtos()
    {
        var canonicalTypes = new[] { typeof(MediaIdentity), typeof(MediaLocationSummary) };

        var leaked = new List<Type>();
        foreach (var type in canonicalTypes)
        {
            foreach (var property in type.GetProperties())
            {
                var propertyType = property.PropertyType;
                var propertyNamespace = propertyType.Namespace ?? string.Empty;
                if (propertyNamespace.StartsWith("ArrTags.Providers.Radarr", StringComparison.Ordinal)
                    || propertyNamespace.StartsWith("ArrTags.Providers.Sonarr", StringComparison.Ordinal))
                {
                    leaked.Add(propertyType);
                }
            }
        }

        Assert.Empty(leaked);
    }

    [Fact]
    public void MediaIdentityRequiresANonEmptyItemIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new MediaIdentity(Guid.Empty, MediaItemType.Movie));
    }

    [Fact]
    public void MediaLibraryResolverIsRegistered()
    {
        var services = new ServiceCollection();
        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IMediaLibraryResolver));
        Assert.Equal(typeof(JellyfinMediaLibraryResolver), descriptor.ImplementationType);
    }

    [Fact]
    public void JellyfinResolverUsesCollectionFoldersAndItemLookups()
    {
        var libraryId = Guid.NewGuid();
        var series = new TestSeries { Id = SeriesId, Name = "Example Series" };
        var item = new TestMovie { Id = ItemId, Name = "Example" };
        var libraryManager = DispatchProxy.Create<ILibraryManager, FakeLibraryManager>();
        var fake = (FakeLibraryManager)(object)libraryManager;
        fake.CollectionFolders = _ => new List<Folder> { new CollectionFolder { Id = libraryId } };
        fake.Items = id => id == SeriesId ? series : null;

        var resolver = new JellyfinMediaLibraryResolver(libraryManager);

        Assert.Equal(libraryId, resolver.ResolveLibraryId(item));
        Assert.Same(series, resolver.ResolveItem(SeriesId));
        Assert.Null(resolver.ResolveItem(Guid.Empty));
    }

    private static TestSeries SeriesItem()
    {
        var series = new TestSeries { Id = SeriesId, Name = "Example Series" };
        series.ProviderIds["Tvdb"] = "12345";
        return series;
    }

    private static MediaIdentity Identity(MediaItemType itemType, Guid? libraryId)
    {
        return new MediaIdentity(Guid.NewGuid(), itemType, libraryId);
    }

    private static PluginConfigurationSnapshot BuildConfiguration(
        IEnumerable<string>? libraries = null,
        bool badgeMoviePosters = true,
        bool badgeEpisodePosters = true)
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = badgeMoviePosters,
            BadgeEpisodePosters = badgeEpisodePosters,
        };

        if (libraries is not null)
        {
            foreach (var library in libraries)
            {
                configuration.EnabledLibraries.Add(library);
            }
        }

        return PluginConfigurationSnapshot.From(configuration);
    }

    private static MediaSourceInfo Source(
        string id,
        string path,
        MediaProtocol protocol,
        long? size = null,
        string? etag = null)
    {
        return new MediaSourceInfo
        {
            Id = id,
            Path = path,
            Protocol = protocol,
            Size = size,
            ETag = etag,
        };
    }

    public class FakeLibraryManager : DispatchProxy
    {
        public Func<BaseItem, List<Folder>>? CollectionFolders { get; set; }

        public Func<Guid, BaseItem?>? Items { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetCollectionFolders))
            {
                return CollectionFolders?.Invoke((BaseItem)args![0]!) ?? new List<Folder>();
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetItemById))
            {
                return Items?.Invoke((Guid)args![0]!);
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private sealed class FakeMediaLibraryResolver : IMediaLibraryResolver
    {
        public Dictionary<Guid, Guid> LibraryIds { get; } = new();

        public Dictionary<Guid, BaseItem> Items { get; } = new();

        public Guid? ResolveLibraryId(BaseItem item)
        {
            return LibraryIds.TryGetValue(item.Id, out var libraryId) ? libraryId : null;
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

    private class TestSeason : Season
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
