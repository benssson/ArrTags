using System;
using System.Collections.Generic;
using ArrTags.Metadata;

namespace ArrTags.Providers.Sonarr;

/// <summary>
/// Maps validated Sonarr series, episode, and episode-file resources into the
/// canonical <see cref="BadgeMetadata"/> model. Only the validated current
/// episode file supplies badge-relevant values; the series quality profile is
/// requested policy and is deliberately never mapped as actual quality.
/// Provider DTOs do not leave this boundary.
/// </summary>
public static class SonarrMetadataMapper
{
    /// <summary>
    /// Maps a Sonarr series into a series-only, connection-scoped identity.
    /// </summary>
    /// <param name="connection">The Sonarr connection scope.</param>
    /// <param name="series">The validated Sonarr series resource.</param>
    /// <returns>The connection-scoped series identity.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection is not a Sonarr connection or the series identifier is invalid.</exception>
    public static SonarrIdentity MapSeriesIdentity(ArrConnection connection, SonarrSeriesResource series)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(series);
        EnsureConnection(connection);
        EnsureSeries(series);

        return new SonarrIdentity(connection.ConnectionId, series.Id);
    }

    /// <summary>
    /// Maps a validated Sonarr episode into its connection-scoped identity. The
    /// current file identity is derived from the authoritative
    /// <c>episodeFileId</c> and is explicitly absent when the episode has no
    /// current file.
    /// </summary>
    /// <param name="connection">The Sonarr connection scope.</param>
    /// <param name="series">The series the episode belongs to.</param>
    /// <param name="episode">The validated Sonarr episode resource.</param>
    /// <returns>The connection-scoped episode identity.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection, series, or episode identifiers are inconsistent.</exception>
    public static SonarrIdentity MapEpisodeIdentity(
        ArrConnection connection,
        SonarrSeriesResource series,
        SonarrEpisodeResource episode)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episode);
        EnsureConnection(connection);
        EnsureSeries(series);
        EnsureEpisodeForSeries(series, episode);

        var fileIdentity = episode.EpisodeFileId is int fileId && fileId > 0 && episode.HasFile != false
            ? ArrFileIdentity.Present(fileId)
            : ArrFileIdentity.Absent;

        return new SonarrIdentity(connection.ConnectionId, series.Id, episode.Id, fileIdentity);
    }

    /// <summary>
    /// Maps a validated Sonarr episode and its current episode file into
    /// canonical badge metadata. The file is used only when its identifier
    /// matches the authoritative <c>episodeFileId</c>; otherwise the metadata
    /// carries the episode identity with unknown technical values.
    /// </summary>
    /// <param name="connection">The Sonarr connection scope.</param>
    /// <param name="series">The series the episode belongs to.</param>
    /// <param name="episode">The validated Sonarr episode resource.</param>
    /// <param name="episodeFile">The validated current episode file, when available.</param>
    /// <param name="observedAt">When the source observation was obtained.</param>
    /// <returns>The canonical badge metadata for the episode.</returns>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The connection, series, or episode identifiers are inconsistent.</exception>
    public static BadgeMetadata Map(
        ArrConnection connection,
        SonarrSeriesResource series,
        SonarrEpisodeResource episode,
        SonarrEpisodeFileResource? episodeFile,
        DateTimeOffset observedAt)
    {
        var identity = MapEpisodeIdentity(connection, series, episode);
        var file = ResolveCurrentFile(identity, episodeFile);
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
        if (connection.Provider.Kind != ArrProviderKind.Sonarr)
        {
            throw new ArgumentException("The connection is not a Sonarr connection.", nameof(connection));
        }
    }

    private static void EnsureSeries(SonarrSeriesResource series)
    {
        if (series.Id <= 0)
        {
            throw new ArgumentException("A positive Sonarr series identifier is required.", nameof(series));
        }
    }

    private static void EnsureEpisodeForSeries(SonarrSeriesResource series, SonarrEpisodeResource episode)
    {
        if (episode.Id <= 0)
        {
            throw new ArgumentException("A positive Sonarr episode identifier is required.", nameof(episode));
        }

        if (episode.SeriesId != series.Id)
        {
            throw new ArgumentException("The episode does not belong to the supplied series.", nameof(episode));
        }
    }

    private static SonarrEpisodeFileResource? ResolveCurrentFile(SonarrIdentity identity, SonarrEpisodeFileResource? episodeFile)
    {
        return identity.EpisodeFileIdentity is { Presence: ArrFilePresence.Present, FileId: int fileId }
            && episodeFile is not null
            && episodeFile.Id == fileId
            ? episodeFile
            : null;
    }

    private static ArrQualityDescriptor? MapQuality(SonarrEpisodeFileResource? file)
    {
        var quality = file?.Quality?.Quality;
        if (quality is null)
        {
            return null;
        }

        var label = Clean(quality.Name);
        var source = Clean(quality.Source);
        if (label is null && source is null && quality.Resolution is null && quality.Id is null)
        {
            return null;
        }

        return new ArrQualityDescriptor(label, source, quality.Resolution, null, quality.Id);
    }

    private static ArrResolutionDescriptor? MapResolution(SonarrEpisodeFileResource? file)
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

    private static ArrDynamicRangeDescriptor? MapDynamicRange(SonarrMediaInfoResource? mediaInfo)
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

    private static IReadOnlyList<ArrAudioFeature>? MapAudioFeatures(string? audioCodec)
    {
        var codec = Clean(audioCodec)?.ToUpperInvariant();
        if (codec is null)
        {
            return null;
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

    private static IReadOnlyList<string> MapCustomBadges(IReadOnlyList<SonarrCustomFormatResource>? formats)
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
