using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace ArrTags.Media;

/// <summary>
/// Builds canonical <see cref="MediaIdentity"/> snapshots from supported Jellyfin
/// structural item types. It captures raw Jellyfin numbering, location, and
/// media-source facts and resolves library and parent-series context through the
/// <see cref="IMediaLibraryResolver"/> boundary. It applies no episode-numbering
/// or path-mapping policy.
/// </summary>
public static class MediaIdentityFactory
{
    /// <summary>
    /// Attempts to build a canonical media identity for a Jellyfin item.
    /// </summary>
    /// <param name="item">The Jellyfin item to snapshot.</param>
    /// <param name="resolver">The library and item resolver boundary.</param>
    /// <param name="identity">The snapshot, or <see langword="null"/> when the item type is unsupported or invalid.</param>
    /// <returns><see langword="true"/> when a supported identity was built.</returns>
    /// <exception cref="ArgumentNullException">The item or resolver is <see langword="null"/>.</exception>
    public static bool TryCreate(BaseItem item, IMediaLibraryResolver resolver, out MediaIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(resolver);

        return TryCreateCore(item, resolver, includeParent: true, out identity);
    }

    private static bool TryCreateCore(
        BaseItem item,
        IMediaLibraryResolver resolver,
        bool includeParent,
        out MediaIdentity? identity)
    {
        if (item.Id == Guid.Empty || GetItemType(item) is not MediaItemType itemType)
        {
            identity = null;
            return false;
        }

        MediaIdentity? seriesIdentity = null;
        if (includeParent && TryGetSeriesId(item, out var seriesId))
        {
            var parent = resolver.ResolveItem(seriesId);
            if (parent is not null
                && TryCreateCore(parent, resolver, includeParent: false, out var parentIdentity))
            {
                seriesIdentity = parentIdentity;
            }
        }

        var mediaSources = GetMediaSources(item);
        var location = CreateLocation(item, mediaSources);
        var providerIds = CopyProviderIds(item.ProviderIds);
        var fingerprint = ComputeSourceFingerprint(item, itemType, location, mediaSources);
        var (seasonNumber, episodeNumber, episodeNumberEnd) = GetNumbering(item, itemType);

        identity = new MediaIdentity(
            item.Id,
            itemType,
            resolver.ResolveLibraryId(item),
            providerIds,
            item.Name,
            item.ProductionYear,
            seriesIdentity,
            seasonNumber,
            episodeNumber,
            episodeNumberEnd,
            location,
            fingerprint);
        return true;
    }

    private static MediaItemType? GetItemType(BaseItem item)
    {
        return item switch
        {
            Movie => MediaItemType.Movie,
            Series => MediaItemType.Series,
            Season => MediaItemType.Season,
            Episode => MediaItemType.Episode,
            _ => null,
        };
    }

    private static bool TryGetSeriesId(BaseItem item, out Guid seriesId)
    {
        switch (item)
        {
            case Episode episode when episode.SeriesId != Guid.Empty:
                seriesId = episode.SeriesId;
                return true;
            case Season season when season.SeriesId != Guid.Empty:
                seriesId = season.SeriesId;
                return true;
            default:
                seriesId = Guid.Empty;
                return false;
        }
    }

    private static (int? SeasonNumber, int? EpisodeNumber, int? EpisodeNumberEnd) GetNumbering(
        BaseItem item,
        MediaItemType itemType)
    {
        return itemType switch
        {
            MediaItemType.Season => (item.IndexNumber, null, null),
            MediaItemType.Episode => (
                item.ParentIndexNumber,
                item.IndexNumber,
                item is Episode episode ? episode.IndexNumberEnd : null),
            _ => (null, null, null),
        };
    }

    private static IReadOnlyList<MediaSourceInfo> GetMediaSources(BaseItem item)
    {
        return item is Video video
            ? video.GetMediaSources(false)
            : (IReadOnlyList<MediaSourceInfo>)Array.Empty<MediaSourceInfo>();
    }

    private static MediaLocationSummary CreateLocation(BaseItem item, IReadOnlyList<MediaSourceInfo> mediaSources)
    {
        var isFileProtocol = false;
        string? primaryPath = null;

        foreach (var source in mediaSources)
        {
            primaryPath ??= string.IsNullOrEmpty(source.Path) ? null : source.Path;
            if (source.Protocol == MediaProtocol.File)
            {
                isFileProtocol = true;
            }
        }

        primaryPath ??= string.IsNullOrEmpty(item.Path) ? null : item.Path;

        return new MediaLocationSummary(
            ToLocationKind(item.LocationType),
            isFileProtocol,
            mediaSources.Count,
            primaryPath);
    }

    private static MediaLocationKind ToLocationKind(LocationType locationType)
    {
        return locationType switch
        {
            LocationType.FileSystem => MediaLocationKind.FileSystem,
            LocationType.Remote => MediaLocationKind.Remote,
            LocationType.Virtual => MediaLocationKind.Virtual,
            LocationType.Offline => MediaLocationKind.Offline,
            _ => MediaLocationKind.Unknown,
        };
    }

    private static IReadOnlyDictionary<string, string> CopyProviderIds(IDictionary<string, string>? providerIds)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (providerIds is null)
        {
            return copy;
        }

        foreach (var pair in providerIds)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                copy[pair.Key] = pair.Value;
            }
        }

        return copy;
    }

    private static string ComputeSourceFingerprint(
        BaseItem item,
        MediaItemType itemType,
        MediaLocationSummary location,
        IReadOnlyList<MediaSourceInfo> mediaSources)
    {
        var builder = new StringBuilder();
        builder.Append("item=").Append(item.Id.ToString("N", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("type=").Append(itemType.ToString()).Append('\n');
        builder.Append("location=").Append(location.Kind.ToString()).Append('\n');
        builder.Append("fileProtocol=").Append(location.IsFileProtocol ? "true" : "false").Append('\n');
        builder.Append("path=").Append(location.PrimaryPath ?? string.Empty).Append('\n');
        builder.Append("count=").Append(location.MediaSourceCount.ToString(CultureInfo.InvariantCulture)).Append('\n');

        var ordered = mediaSources
            .OrderBy(source => source.Id, StringComparer.Ordinal)
            .ThenBy(source => source.Path, StringComparer.Ordinal);
        foreach (var source in ordered)
        {
            builder
                .Append(source.Id ?? string.Empty).Append('|')
                .Append(source.Path ?? string.Empty).Append('|')
                .Append(source.Protocol.ToString()).Append('|')
                .Append(source.Size?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append('|')
                .Append(source.ETag ?? string.Empty).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
