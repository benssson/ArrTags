using System;
using System.Collections.Generic;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 2.5 checks for provider-to-canonical metadata mapping. The mappers must
/// produce canonical <see cref="BadgeMetadata"/> from actual file resources,
/// never from quality profiles, and must not expose provider DTOs. These tests
/// require no live Jellyfin or Arr instance.
/// </summary>
public class MetadataMappingTests
{
    private static readonly DateTimeOffset ObservedAt =
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void RadarrMapUsesActualFileQualityAndTechnicalValues()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var movie = BuildMovie(fileId: 42);
        var file = BuildMovieFile(id: 42);

        var metadata = RadarrMetadataMapper.Map(connection, movie, file, ObservedAt);

        var identity = Assert.IsType<RadarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(7, identity.MovieId);
        Assert.Equal(ArrFilePresence.Present, identity.MovieFileIdentity.Presence);
        Assert.Equal(42, identity.MovieFileIdentity.FileId);
        Assert.Equal(ArrProviderKind.Radarr, metadata.Provider.Kind);
        Assert.Equal(connection.Provider.ProviderInstanceId, metadata.Provider.ProviderInstanceId);

        Assert.Equal("Bluray-1080p", metadata.Quality!.Label);
        Assert.Equal("bluray", metadata.Quality.Source);
        Assert.Equal(1080, metadata.Quality.Resolution);
        Assert.Equal("none", metadata.Quality.Modifier);
        Assert.Equal(7, metadata.Quality.ProviderQualityId);

        Assert.Equal(1920, metadata.Resolution!.Width);
        Assert.Equal(1080, metadata.Resolution.Height);
        Assert.Equal(ArrMetadataOrigin.ProviderMediaInfo, metadata.Resolution.Origin);

        Assert.Equal(ArrDynamicRangeKind.Hdr10, metadata.DynamicRange!.Kind);
        Assert.Equal("HDR10", metadata.DynamicRange.Profile);
        Assert.Null(metadata.DolbyVision);

        Assert.Equal("x265", metadata.VideoCodec);
        Assert.Equal("TrueHD Atmos", metadata.AudioCodec);
        Assert.Equal(8, metadata.AudioChannels);
        Assert.NotNull(metadata.AudioFeatures);
        Assert.Contains(ArrAudioFeature.Atmos, metadata.AudioFeatures);

        Assert.Equal("bluray", metadata.Source);
        Assert.True(metadata.UpgradePending);
        Assert.Equal(new[] { "HDR" }, metadata.CustomBadges);
        Assert.False(string.IsNullOrEmpty(metadata.MetadataFingerprint));
    }

    [Fact]
    public void RadarrMapWithoutFileHasAbsentFileIdentityAndUnknownQuality()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var movie = BuildMovie(fileId: 0, hasFile: false);

        var metadata = RadarrMetadataMapper.Map(connection, movie, null, ObservedAt);

        var identity = Assert.IsType<RadarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(ArrFilePresence.Absent, identity.MovieFileIdentity.Presence);
        Assert.Null(identity.MovieFileIdentity.FileId);

        Assert.Null(metadata.Quality);
        Assert.Null(metadata.Resolution);
        Assert.Null(metadata.DynamicRange);
        Assert.Null(metadata.DolbyVision);
        Assert.Null(metadata.VideoCodec);
        Assert.Null(metadata.AudioCodec);
        Assert.Null(metadata.AudioChannels);
        Assert.Null(metadata.AudioFeatures);
        Assert.Null(metadata.UpgradePending);
        Assert.Empty(metadata.CustomBadges);
    }

    [Fact]
    public void RadarrMapWithMismatchedFileResourceKeepsUnknownQuality()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var movie = BuildMovie(fileId: 42);
        var staleFile = BuildMovieFile(id: 99);

        var metadata = RadarrMetadataMapper.Map(connection, movie, staleFile, ObservedAt);

        var identity = Assert.IsType<RadarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(42, identity.MovieFileIdentity.FileId);
        Assert.Null(metadata.Quality);
    }

    [Fact]
    public void SonarrMapUsesValidatedEpisodeFileQuality()
    {
        var connection = BuildConnection(ArrProviderKind.Sonarr);
        var series = BuildSeries();
        var episode = BuildEpisode(fileId: 418);
        var file = BuildEpisodeFile(id: 418);

        var metadata = SonarrMetadataMapper.Map(connection, series, episode, file, ObservedAt);

        var identity = Assert.IsType<SonarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(12, identity.SeriesId);
        Assert.Equal(73, identity.EpisodeId);
        Assert.Equal(ArrFilePresence.Present, identity.EpisodeFileIdentity!.Presence);
        Assert.Equal(418, identity.EpisodeFileIdentity.FileId);

        Assert.Equal("WEBDL-1080p", metadata.Quality!.Label);
        Assert.Equal("web", metadata.Quality.Source);
        Assert.Equal(1080, metadata.Quality.Resolution);
        Assert.Equal(1920, metadata.Resolution!.Width);
        Assert.Equal(1080, metadata.Resolution.Height);
        Assert.Equal(ArrDynamicRangeKind.Hdr10, metadata.DynamicRange!.Kind);
        Assert.Equal("h264", metadata.VideoCodec);
        Assert.Equal("ac3", metadata.AudioCodec);
        Assert.Equal("web", metadata.Source);
        Assert.True(metadata.UpgradePending);
        Assert.Equal(new[] { "HDR" }, metadata.CustomBadges);
    }

    [Fact]
    public void SonarrMapWithoutFileHasAbsentFileIdentityAndUnknownQuality()
    {
        var connection = BuildConnection(ArrProviderKind.Sonarr);
        var series = BuildSeries();
        var episode = BuildEpisode(fileId: 0);

        var metadata = SonarrMetadataMapper.Map(connection, series, episode, null, ObservedAt);

        var identity = Assert.IsType<SonarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(ArrFilePresence.Absent, identity.EpisodeFileIdentity!.Presence);
        Assert.Null(identity.EpisodeFileIdentity.FileId);
        Assert.Null(metadata.Quality);
        Assert.Null(metadata.VideoCodec);
    }

    [Fact]
    public void SonarrMapWithMismatchedEpisodeFileKeepsUnknownQuality()
    {
        var connection = BuildConnection(ArrProviderKind.Sonarr);
        var series = BuildSeries();
        var episode = BuildEpisode(fileId: 418);
        var staleFile = BuildEpisodeFile(id: 999);

        var metadata = SonarrMetadataMapper.Map(connection, series, episode, staleFile, ObservedAt);

        var identity = Assert.IsType<SonarrIdentity>(metadata.RecordIdentity);
        Assert.Equal(418, identity.EpisodeFileIdentity!.FileId);
        Assert.Null(metadata.Quality);
    }

    [Fact]
    public void SonarrMapRejectsEpisodeFromAnotherSeries()
    {
        var connection = BuildConnection(ArrProviderKind.Sonarr);
        var series = BuildSeries();
        var episode = BuildEpisode(fileId: 418, seriesId: 99);

        Assert.Throws<ArgumentException>(() => SonarrMetadataMapper.Map(connection, series, episode, null, ObservedAt));
    }

    [Fact]
    public void MappersRejectTheWrongProviderConnection()
    {
        var radarr = BuildConnection(ArrProviderKind.Radarr);
        var sonarr = BuildConnection(ArrProviderKind.Sonarr);

        Assert.Throws<ArgumentException>(
            () => RadarrMetadataMapper.MapIdentity(sonarr, BuildMovie(fileId: 0)));
        Assert.Throws<ArgumentException>(
            () => RadarrMetadataMapper.Map(sonarr, BuildMovie(fileId: 0), null, ObservedAt));
        Assert.Throws<ArgumentException>(
            () => SonarrMetadataMapper.Map(radarr, BuildSeries(), BuildEpisode(fileId: 0), null, ObservedAt));
    }

    [Fact]
    public void MetadataFingerprintIsStableAndIdentitySensitive()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var movie = BuildMovie(fileId: 42);
        var file = BuildMovieFile(id: 42);

        var first = RadarrMetadataMapper.Map(connection, movie, file, ObservedAt);
        var laterObservation = RadarrMetadataMapper.Map(connection, movie, file, ObservedAt.AddHours(5));
        var changedFile = RadarrMetadataMapper.Map(connection, movie, BuildMovieFile(id: 42, label: "WEBDL-720p"), ObservedAt);

        Assert.Equal(first.MetadataFingerprint, laterObservation.MetadataFingerprint);
        Assert.NotEqual(first.MetadataFingerprint, changedFile.MetadataFingerprint);
    }

    [Fact]
    public void MetadataFingerprintDoesNotLeakCredential()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr, apiKey: "radarr-super-secret");
        var metadata = RadarrMetadataMapper.Map(connection, BuildMovie(fileId: 42), BuildMovieFile(id: 42), ObservedAt);

        Assert.DoesNotContain("radarr-super-secret", metadata.MetadataFingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomBadgesPreserveOrderAndDropBlankValues()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var file = BuildMovieFile(
            id: 42,
            customFormats: new[]
            {
                new RadarrCustomFormatResource { Name = "Second" },
                new RadarrCustomFormatResource { Name = " " },
                new RadarrCustomFormatResource { Name = "First" },
            });

        var metadata = RadarrMetadataMapper.Map(connection, BuildMovie(fileId: 42), file, ObservedAt);

        Assert.Equal(new[] { "Second", "First" }, metadata.CustomBadges);
    }

    [Fact]
    public void RadarrAudioFeaturesAreUnknownWhenNoCodecIsReported()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var file = new RadarrMovieFileResource
        {
            Id = 42,
            MediaInfo = new RadarrMediaInfoResource { VideoCodec = "x265" },
        };

        var metadata = RadarrMetadataMapper.Map(connection, BuildMovie(fileId: 42), file, ObservedAt);

        Assert.Null(metadata.AudioFeatures);
    }

    [Fact]
    public void RadarrEmptyAudioFeaturesMeansReportedWithoutKnownFeature()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var file = new RadarrMovieFileResource
        {
            Id = 42,
            MediaInfo = new RadarrMediaInfoResource { AudioCodec = "aac" },
        };

        var metadata = RadarrMetadataMapper.Map(connection, BuildMovie(fileId: 42), file, ObservedAt);

        Assert.NotNull(metadata.AudioFeatures);
        Assert.Empty(metadata.AudioFeatures);
    }

    [Fact]
    public void SonarrAudioFeaturesAreUnknownWhenNoCodecIsReported()
    {
        var connection = BuildConnection(ArrProviderKind.Sonarr);
        var file = new SonarrEpisodeFileResource
        {
            Id = 418,
            SeriesId = 12,
            MediaInfo = new SonarrMediaInfoResource { VideoCodec = "h264" },
        };

        var metadata = SonarrMetadataMapper.Map(
            connection, BuildSeries(), BuildEpisode(fileId: 418), file, ObservedAt);

        Assert.Null(metadata.AudioFeatures);
    }

    [Fact]
    public void FingerprintDistinguishesUnknownFromEmptyAudioFeatures()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var identity = RadarrMetadataMapper.MapIdentity(connection, BuildMovie(fileId: 42));

        var unknown = new BadgeMetadata(connection.Provider, identity, ObservedAt);
        var none = new BadgeMetadata(
            connection.Provider,
            identity,
            ObservedAt,
            audioFeatures: Array.Empty<ArrAudioFeature>());

        Assert.Null(unknown.AudioFeatures);
        Assert.NotNull(none.AudioFeatures);
        Assert.Empty(none.AudioFeatures);
        Assert.NotEqual(unknown.MetadataFingerprint, none.MetadataFingerprint);
    }

    [Fact]
    public void CustomBadgesAreBoundedByCountInProviderOrder()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var formats = Enumerable.Range(0, BadgeMetadata.MaxCustomBadgeCount + 5)
            .Select(index => new RadarrCustomFormatResource { Name = "Format " + index })
            .ToArray();

        var metadata = RadarrMetadataMapper.Map(
            connection,
            BuildMovie(fileId: 42),
            BuildMovieFile(id: 42, customFormats: formats),
            ObservedAt);

        Assert.Equal(BadgeMetadata.MaxCustomBadgeCount, metadata.CustomBadges.Count);
        Assert.Equal("Format 0", metadata.CustomBadges[0]);
        Assert.Equal(
            "Format " + (BadgeMetadata.MaxCustomBadgeCount - 1),
            metadata.CustomBadges[^1]);
    }

    [Fact]
    public void CustomBadgesAreBoundedByLength()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var formats = new[]
        {
            new RadarrCustomFormatResource
            {
                Name = new string('x', BadgeMetadata.MaxCustomBadgeLength + 10),
            },
        };

        var metadata = RadarrMetadataMapper.Map(
            connection,
            BuildMovie(fileId: 42),
            BuildMovieFile(id: 42, customFormats: formats),
            ObservedAt);

        var badge = Assert.Single(metadata.CustomBadges);
        Assert.Equal(BadgeMetadata.MaxCustomBadgeLength, badge.Length);
    }

    [Fact]
    public void CustomBadgesDropControlCharacters()
    {
        var connection = BuildConnection(ArrProviderKind.Radarr);
        var formats = new[] { new RadarrCustomFormatResource { Name = "Bad\n\tName" } };

        var metadata = RadarrMetadataMapper.Map(
            connection,
            BuildMovie(fileId: 42),
            BuildMovieFile(id: 42, customFormats: formats),
            ObservedAt);

        Assert.Equal(new[] { "BadName" }, metadata.CustomBadges);
    }

    [Fact]
    public void CanonicalMetadataTypesDoNotExposeProviderDtos()
    {
        var canonicalTypes = new[]
        {
            typeof(BadgeMetadata),
            typeof(ArrQualityDescriptor),
            typeof(ArrResolutionDescriptor),
            typeof(ArrDynamicRangeDescriptor),
        };

        var leaked = canonicalTypes
            .SelectMany(type => type.GetProperties())
            .Select(property => property.PropertyType)
            .Where(IsProviderDto)
            .ToList();

        Assert.Empty(leaked);
    }

    [Fact]
    public void CanonicalMetadataTypesDoNotExposeQualityProfiles()
    {
        var profileProperties = typeof(BadgeMetadata).GetProperties()
            .Concat(typeof(ArrQualityDescriptor).GetProperties())
            .Where(property => property.Name.Contains("Profile", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(profileProperties);
    }

    private static bool IsProviderDto(Type type)
    {
        var typeNamespace = type.Namespace ?? string.Empty;
        return typeNamespace.StartsWith("ArrTags.Providers.Radarr", StringComparison.Ordinal)
            || typeNamespace.StartsWith("ArrTags.Providers.Sonarr", StringComparison.Ordinal);
    }

    private static RadarrMovieResource BuildMovie(int fileId, bool? hasFile = null)
    {
        return new RadarrMovieResource
        {
            Id = 7,
            Title = "Example",
            QualityProfileId = 99,
            HasFile = hasFile ?? fileId > 0,
            MovieFileId = fileId,
        };
    }

    private static RadarrMovieFileResource BuildMovieFile(
        int id,
        string label = "Bluray-1080p",
        IReadOnlyList<RadarrCustomFormatResource>? customFormats = null)
    {
        return new RadarrMovieFileResource
        {
            Id = id,
            MovieId = 7,
            Quality = new RadarrQualityModel
            {
                Quality = new RadarrQuality
                {
                    Id = 7,
                    Name = label,
                    Source = "bluray",
                    Resolution = 1080,
                    Modifier = "none",
                },
                Revision = new RadarrRevision { Version = 1, IsRepack = false },
            },
            CustomFormats = customFormats ?? new[] { new RadarrCustomFormatResource { Id = 1, Name = "HDR" } },
            CustomFormatScore = 100,
            QualityCutoffNotMet = true,
            MediaInfo = new RadarrMediaInfoResource
            {
                VideoCodec = "x265",
                AudioCodec = "TrueHD Atmos",
                AudioChannels = 8,
                VideoDynamicRange = "HDR",
                VideoDynamicRangeType = "HDR10",
                Width = 1920,
                Height = 1080,
                Resolution = "1920x1080",
            },
        };
    }

    private static SonarrSeriesResource BuildSeries()
    {
        return new SonarrSeriesResource
        {
            Id = 12,
            Title = "Example",
            QualityProfileId = 99,
            ProfileName = "HD-1080p",
            TvdbId = 1234567,
        };
    }

    private static SonarrEpisodeResource BuildEpisode(int fileId, int seriesId = 12)
    {
        return new SonarrEpisodeResource
        {
            Id = 73,
            SeriesId = seriesId,
            SeasonNumber = 2,
            EpisodeNumber = 4,
            EpisodeFileId = fileId,
            HasFile = fileId > 0,
        };
    }

    private static SonarrEpisodeFileResource BuildEpisodeFile(int id)
    {
        return new SonarrEpisodeFileResource
        {
            Id = id,
            SeriesId = 12,
            Quality = new SonarrQualityModel
            {
                Quality = new SonarrQuality
                {
                    Id = 3,
                    Name = "WEBDL-1080p",
                    Source = "web",
                    Resolution = 1080,
                },
                Revision = new SonarrRevision { Version = 1, Real = 0, IsRepack = false },
            },
            QualityCutoffNotMet = true,
            CustomFormats = new[] { new SonarrCustomFormatResource { Id = 1, Name = "HDR" } },
            CustomFormatScore = 50,
            MediaInfo = new SonarrMediaInfoResource
            {
                VideoCodec = "h264",
                AudioCodec = "ac3",
                Width = 1920,
                Height = 1080,
                VideoDynamicRangeType = "HDR10",
            },
        };
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
}
