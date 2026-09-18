using System;
using System.Collections.Generic;
using ArrTags.Metadata;

namespace ArrTags.Providers.Radarr;

/// <summary>
/// Maps validated Radarr movie and movie-file resources into the canonical
/// <see cref="BadgeMetadata"/> model. Only the actual file resource supplies
/// badge-relevant values; the movie quality profile is requested policy and is
/// deliberately never mapped as actual quality. Provider DTOs do not leave this
/// boundary.
/// </summary>
public static class RadarrMetadataMapper
{
    /// <summary>
    /// Maps a Radarr movie into its connection-scoped identity. The current file
    /// identity is derived from the authoritative <c>movieFileId</c> and is
    /// explicitly absent when the movie has no imported file.
    /// </summary>
    /// <param name="connection">The Radarr connection scope.</param>
    /// <param name="movie">The validated Radarr movie resource.</param>
    /// <returns>The connection-scoped Radarr identity.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection is not a Radarr connection or the movie identifier is invalid.</exception>
    public static RadarrIdentity MapIdentity(ArrConnection connection, RadarrMovieResource movie)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(movie);
        EnsureConnection(connection);

        if (movie.Id <= 0)
        {
            throw new ArgumentException("A positive Radarr movie identifier is required.", nameof(movie));
        }

        var fileIdentity = movie.MovieFileId is int fileId && fileId > 0
            ? ArrFileIdentity.Present(fileId)
            : ArrFileIdentity.Absent;

        return new RadarrIdentity(connection.ConnectionId, movie.Id, fileIdentity);
    }

    /// <summary>
    /// Maps a validated Radarr movie and its current movie file into canonical
    /// badge metadata. The file is used only when its identifier matches the
    /// authoritative <c>movieFileId</c>; otherwise the metadata carries an
    /// explicit present file identity with unknown technical values.
    /// </summary>
    /// <param name="connection">The Radarr connection scope.</param>
    /// <param name="movie">The validated Radarr movie resource.</param>
    /// <param name="movieFile">The dedicated movie-file resource, when available.</param>
    /// <param name="observedAt">When the source observation was obtained.</param>
    /// <returns>The canonical badge metadata for the movie.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection is not a Radarr connection or the movie identifier is invalid.</exception>
    public static BadgeMetadata Map(
        ArrConnection connection,
        RadarrMovieResource movie,
        RadarrMovieFileResource? movieFile,
        DateTimeOffset observedAt)
    {
        var identity = MapIdentity(connection, movie);
        var file = ResolveCurrentFile(identity, movieFile);
        var dynamicRange = MapDynamicRange(file?.MediaInfo);

        return new BadgeMetadata(
            connection.Provider,
            identity,
            observedAt,
            quality: MapQuality(file),
            resolution: MapResolution(file),
            dynamicRange: dynamicRange,
            dolbyVision: MapDolbyVision(dynamicRange),
            videoCodec: Clean(file?.MediaInfo?.VideoCodec),
            audioCodec: Clean(file?.MediaInfo?.AudioCodec),
            audioChannels: file?.MediaInfo?.AudioChannels,
            audioFeatures: MapAudioFeatures(file?.MediaInfo?.AudioCodec),
            source: Clean(file?.Quality?.Quality?.Source),
            upgradePending: file?.QualityCutoffNotMet,
            customBadges: MapCustomBadges(file?.CustomFormats));
    }

    private static void EnsureConnection(ArrConnection connection)
    {
        if (connection.Provider.Kind != ArrProviderKind.Radarr)
        {
            throw new ArgumentException("The connection is not a Radarr connection.", nameof(connection));
        }
    }

    private static RadarrMovieFileResource? ResolveCurrentFile(RadarrIdentity identity, RadarrMovieFileResource? movieFile)
    {
        return identity.MovieFileIdentity is { Presence: ArrFilePresence.Present, FileId: int fileId }
            && movieFile is not null
            && movieFile.Id == fileId
            ? movieFile
            : null;
    }

    private static ArrQualityDescriptor? MapQuality(RadarrMovieFileResource? file)
    {
        var quality = file?.Quality?.Quality;
        if (quality is null)
        {
            return null;
        }

        var label = Clean(quality.Name);
        var source = Clean(quality.Source);
        var modifier = Clean(quality.Modifier);
        if (label is null && source is null && modifier is null && quality.Resolution is null && quality.Id is null)
        {
            return null;
        }

        return new ArrQualityDescriptor(label, source, quality.Resolution, modifier, quality.Id);
    }

    private static ArrResolutionDescriptor? MapResolution(RadarrMovieFileResource? file)
    {
        if (file is null)
        {
            return null;
        }

        var mediaInfo = file.MediaInfo;
        var label = Clean(mediaInfo?.Resolution);
        if (mediaInfo?.Width is int width && mediaInfo.Height is int height)
        {
            return new ArrResolutionDescriptor(width, height, label, ArrMetadataOrigin.ProviderMediaInfo);
        }

        if (file.Quality?.Quality?.Resolution is int resolution && resolution > 0)
        {
            return new ArrResolutionDescriptor(null, resolution, label, ArrMetadataOrigin.ProviderQuality);
        }

        return label is null
            ? null
            : new ArrResolutionDescriptor(null, null, label, ArrMetadataOrigin.ProviderMediaInfo);
    }

    private static ArrDynamicRangeDescriptor? MapDynamicRange(RadarrMediaInfoResource? mediaInfo)
    {
        if (mediaInfo is null)
        {
            return null;
        }

        var family = Clean(mediaInfo.VideoDynamicRange);
        var profile = Clean(mediaInfo.VideoDynamicRangeType);
        if (family is null && profile is null)
        {
            return null;
        }

        return new ArrDynamicRangeDescriptor(
            ClassifyDynamicRange(family, profile),
            profile ?? family,
            ArrMetadataOrigin.ProviderMediaInfo);
    }

    private static ArrDynamicRangeKind ClassifyDynamicRange(string? family, string? profile)
    {
        var combined = string.Concat(family, " ", profile).ToUpperInvariant();
        if (combined.Contains("DOLBY", StringComparison.Ordinal) || combined.Contains("DOVI", StringComparison.Ordinal))
        {
            return ArrDynamicRangeKind.DolbyVision;
        }

        if (combined.Contains("HDR10+", StringComparison.Ordinal) || combined.Contains("HDR10PLUS", StringComparison.Ordinal))
        {
            return ArrDynamicRangeKind.Hdr10Plus;
        }

        if (combined.Contains("HDR10", StringComparison.Ordinal))
        {
            return ArrDynamicRangeKind.Hdr10;
        }

        if (combined.Contains("HLG", StringComparison.Ordinal))
        {
            return ArrDynamicRangeKind.Hlg;
        }

        if (combined.Contains("HDR", StringComparison.Ordinal))
        {
            return ArrDynamicRangeKind.Hdr;
        }

        return combined.Contains("SDR", StringComparison.Ordinal)
            ? ArrDynamicRangeKind.Sdr
            : ArrDynamicRangeKind.Unknown;
    }

    private static bool? MapDolbyVision(ArrDynamicRangeDescriptor? dynamicRange)
    {
        return dynamicRange is not null && dynamicRange.Kind == ArrDynamicRangeKind.DolbyVision
            ? true
            : null;
    }

    private static IReadOnlyList<ArrAudioFeature> MapAudioFeatures(string? audioCodec)
    {
        var codec = Clean(audioCodec)?.ToUpperInvariant();
        if (codec is null)
        {
            return Array.Empty<ArrAudioFeature>();
        }

        var features = new List<ArrAudioFeature>();
        if (codec.Contains("ATMOS", StringComparison.Ordinal))
        {
            features.Add(ArrAudioFeature.Atmos);
        }

        if (codec.Contains("DTS:X", StringComparison.Ordinal) || codec.Contains("DTSX", StringComparison.Ordinal))
        {
            features.Add(ArrAudioFeature.DtsX);
        }

        if (codec.Contains("DTS-HD", StringComparison.Ordinal) || codec.Contains("DTSHD", StringComparison.Ordinal))
        {
            features.Add(ArrAudioFeature.DtsHd);
        }

        if (codec.Contains("DTS", StringComparison.Ordinal))
        {
            features.Add(ArrAudioFeature.Dts);
        }

        return features;
    }

    private static IReadOnlyList<string> MapCustomBadges(IReadOnlyList<RadarrCustomFormatResource>? formats)
    {
        if (formats is null)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(formats.Count);
        foreach (var format in formats)
        {
            var name = Clean(format?.Name);
            if (name is not null)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
