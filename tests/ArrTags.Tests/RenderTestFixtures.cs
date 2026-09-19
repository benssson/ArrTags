using System;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Rendering;

namespace ArrTags.Tests;

/// <summary>
/// Provider-neutral fixtures shared by the task 4.9 renderer tests. They build
/// canonical identities, a matched result, metadata, and a render request
/// without any provider DTO or host object.
/// </summary>
internal static class RenderTestFixtures
{
    public static readonly Guid ItemId = new Guid("33333333-3333-3333-3333-333333333333");

    public static readonly DateTimeOffset ObservedAt =
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static MediaIdentity BuildMovieIdentity()
    {
        return new MediaIdentity(ItemId, MediaItemType.Movie);
    }

    public static MediaIdentity BuildEpisodeIdentity()
    {
        return new MediaIdentity(ItemId, MediaItemType.Episode, seasonNumber: 1, episodeNumber: 2);
    }

    public static MediaMatch BuildMatch(MediaIdentity identity, MediaMatchStatus status = MediaMatchStatus.Matched)
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");

        if (status != MediaMatchStatus.Matched)
        {
            return new MediaMatch(identity, provider, connectionId, status, MediaMatchMethod.None);
        }

        var recordIdentity = new RadarrIdentity(connectionId, 7, ArrFileIdentity.Present(42));
        return new MediaMatch(
            identity,
            provider,
            connectionId,
            status,
            MediaMatchMethod.ProviderId,
            recordIdentity);
    }

    public static BadgeMetadata BuildMetadata(
        string? videoCodec = "x265",
        string? source = "WEB-DL",
        bool? upgradePending = null,
        string? qualityLabel = "Bluray-1080p")
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
        var recordIdentity = new RadarrIdentity(connectionId, 7, ArrFileIdentity.Present(42));

        return new BadgeMetadata(
            provider,
            recordIdentity,
            ObservedAt,
            quality: qualityLabel is null ? null : new ArrQualityDescriptor(qualityLabel, "bluray", 1080, "none", 7),
            resolution: new ArrResolutionDescriptor(1920, 1080, "1080p", ArrMetadataOrigin.ProviderMediaInfo),
            videoCodec: videoCodec,
            source: source,
            upgradePending: upgradePending);
    }

    public static BadgeMetadata BuildEmptyMetadata()
    {
        var provider = new ArrProvider(ArrProviderKind.Radarr, "radarr:test");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.local:7878");
        var recordIdentity = new RadarrIdentity(connectionId, 7, ArrFileIdentity.Present(42));

        return new BadgeMetadata(provider, recordIdentity, ObservedAt);
    }

    public static RenderRequest BuildRequest(
        SourceImageInput? source,
        MediaIdentity? identity = null,
        MediaMatch? match = null,
        BadgeMetadata? metadata = null,
        System.Collections.Generic.IReadOnlyList<BadgeDefinition>? definitions = null,
        RenderOutputPolicy? policy = null,
        OperationalLimits? limits = null)
    {
        var resolvedIdentity = identity ?? BuildMovieIdentity();
        return new RenderRequest(
            source,
            resolvedIdentity,
            match ?? BuildMatch(resolvedIdentity),
            metadata,
            definitions ?? BadgeDefinition.V1Default,
            "CONFIG-TEST",
            policy,
            limits);
    }
}
