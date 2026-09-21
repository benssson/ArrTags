using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace ArrTags.Tests;

/// <summary>
/// Shared doubles and builders for the task 6.3 metadata state and
/// reconciliation tests. They require no live Jellyfin or Arr instance.
/// </summary>
internal sealed class ReconciliationLibraryResolver : IMediaLibraryResolver
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

internal sealed class ReconciliationTestMovie : Movie
{
    public LocationType Location { get; set; } = LocationType.FileSystem;

    public IReadOnlyList<MediaSourceInfo> Sources { get; set; } = Array.Empty<MediaSourceInfo>();

    public override LocationType LocationType => Location;

    public override IReadOnlyList<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution) => Sources;
}

internal sealed class ReconciliationTestSeries : Series
{
    public LocationType Location { get; set; } = LocationType.FileSystem;

    public override LocationType LocationType => Location;
}

internal sealed class ReconciliationTestEpisode : Episode
{
    public LocationType Location { get; set; } = LocationType.FileSystem;

    public IReadOnlyList<MediaSourceInfo> Sources { get; set; } = Array.Empty<MediaSourceInfo>();

    public override LocationType LocationType => Location;

    public override IReadOnlyList<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution) => Sources;
}

/// <summary>
/// A configurable <see cref="IArrMetadataReader"/> double. The <see cref="OnRead"/>
/// hook lets a test change the world (for example remove the item or replace the
/// configuration) while the work is processing.
/// </summary>
internal sealed class FakeMetadataReader : IArrMetadataReader
{
    public ArrProviderKind Kind { get; set; } = ArrProviderKind.Radarr;

    public Func<MediaIdentity, ArrConnection, CancellationToken, ArrMetadataReadResult> Handler { get; set; } =
        static (_, _, _) => throw new InvalidOperationException("No reader handler was configured.");

    public ArrMetadataReadResult? Result { get; set; }

    public Action? OnRead { get; set; }

    public Task<ArrMetadataReadResult> ReadAsync(
        MediaIdentity identity,
        ArrConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OnRead?.Invoke();
        var result = Result ?? Handler(identity, connection, cancellationToken);
        return Task.FromResult(result);
    }
}

/// <summary>
/// Builders for canonical identities, matches, badge metadata, and metadata
/// state entries shared by the task 6.3 tests.
/// </summary>
internal static class ReconciliationFixtures
{
    public static readonly Guid ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static readonly Guid LibraryId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
    public static readonly DateTimeOffset ObservedAt = DateTimeOffset.UnixEpoch.AddDays(10);

    public static ArrConnectionId RadarrConnectionId { get; } =
        ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.test");

    public static ArrProvider RadarrProvider { get; } =
        new ArrProvider(ArrProviderKind.Radarr, RadarrConnectionId.Value);

    public static MediaSourceInfo FileSource(string path)
    {
        return new MediaSourceInfo
        {
            Id = "source-1",
            Path = path,
            Protocol = MediaProtocol.File,
            Size = 1024,
            ETag = "etag-1",
        };
    }

    public static ReconciliationTestMovie Movie(Guid itemId, Guid libraryId)
    {
        var movie = new ReconciliationTestMovie
        {
            Id = itemId,
            Name = "Example Movie",
            ProductionYear = 2021,
            Path = "/media/movies/example.mkv",
            Location = LocationType.FileSystem,
            Sources = new[] { FileSource("/media/movies/example.mkv") },
        };
        movie.ProviderIds["Tmdb"] = "603";
        movie.ProviderIds["Imdb"] = "tt0133093";
        return movie;
    }

    public static ReconciliationTestEpisode Episode(Guid itemId, Guid libraryId, Guid seriesId)
    {
        var episode = new ReconciliationTestEpisode
        {
            Id = itemId,
            Name = "Example Episode",
            SeriesId = seriesId,
            ParentIndexNumber = 2,
            IndexNumber = 5,
            Path = "/media/tv/example/s02e05.mkv",
            Location = LocationType.FileSystem,
            Sources = new[] { FileSource("/media/tv/example/s02e05.mkv") },
        };
        episode.ProviderIds["Tvdb"] = "999001";
        return episode;
    }

    public static ReconciliationTestSeries Series(Guid seriesId, Guid libraryId)
    {
        var series = new ReconciliationTestSeries
        {
            Id = seriesId,
            Name = "Example Series",
            Path = "/media/tv/example",
        };
        series.ProviderIds["Tvdb"] = "12345";
        return series;
    }

    public static MediaIdentity MovieIdentity(Guid? itemId = null)
    {
        return new MediaIdentity(
            itemId ?? ItemId,
            MediaItemType.Movie,
            LibraryId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tmdb"] = "603",
                ["Imdb"] = "tt0133093",
            },
            "Example Movie",
            2021,
            mediaLocation: new MediaLocationSummary(
                MediaLocationKind.FileSystem,
                isFileProtocol: true,
                mediaSourceCount: 1,
                primaryPath: "/media/movies/example.mkv"),
            sourceFingerprint: "SOURCE-FINGERPRINT");
    }

    public static MediaMatch MatchedMovieMatch(
        MediaIdentity identity,
        ArrConnectionId? connectionId = null,
        int movieId = 42,
        int? movieFileId = 84)
    {
        var scope = connectionId ?? RadarrConnectionId;
        var provider = new ArrProvider(ArrProviderKind.Radarr, scope.Value);
        var record = new RadarrIdentity(
            scope,
            movieId,
            movieFileId is int fileId ? ArrFileIdentity.Present(fileId) : ArrFileIdentity.Absent);

        return new MediaMatch(
            identity,
            provider,
            scope,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            record,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tmdb"] = "603" });
    }

    public static BadgeMetadata MovieMetadata(
        MediaIdentity identity,
        ArrRecordIdentity? recordIdentity = null,
        bool includeAudioFeatures = true,
        bool unknownAudioFeatures = false)
    {
        var record = recordIdentity ?? MatchedMovieMatch(identity).RecordIdentity!;
        var provider = new ArrProvider(ArrProviderKind.Radarr, record.ConnectionId.Value);

        return new BadgeMetadata(
            provider,
            record,
            ObservedAt,
            quality: new ArrQualityDescriptor("Bluray-1080p", "bluray", 1080, "Remux", 7),
            resolution: new ArrResolutionDescriptor(1920, 1080, "1080p", ArrMetadataOrigin.ProviderMediaInfo),
            dynamicRange: new ArrDynamicRangeDescriptor(ArrDynamicRangeKind.Hdr10, "HDR10", ArrMetadataOrigin.ProviderMediaInfo),
            dolbyVision: null,
            videoCodec: "h264",
            audioCodec: "EAC3",
            audioChannels: 6,
            audioFeatures: unknownAudioFeatures
                ? null
                : includeAudioFeatures ? new[] { ArrAudioFeature.Atmos } : Array.Empty<ArrAudioFeature>(),
            source: "bluray",
            upgradePending: true,
            customBadges: new[] { "CustomFormatA" });
    }

    public static MetadataStateEntry MatchedEntry(MediaIdentity? identity = null, BadgeMetadata? metadata = null)
    {
        var subject = identity ?? MovieIdentity();
        var match = MatchedMovieMatch(subject);
        var badge = metadata ?? MovieMetadata(subject, match.RecordIdentity);
        return MetadataStateEntry.From(subject, match, badge, ObservedAt);
    }

    public static ConfigurationSnapshotService Configuration(bool radarrEnabled = true, bool badgeMoviePosters = true)
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = badgeMoviePosters,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = radarrEnabled,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
        };

        return new ConfigurationSnapshotService(configuration);
    }
}
