using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.State;
using ArrTags.Updates;
using ArrTags.Webhooks;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused tests for the task 6.7 provider-record-to-Jellyfin resolution and the
/// hosted webhook intake consumer: a webhook is a bounded hint that resolves only
/// to items ArrTags already associated with the advertised provider record, is
/// bounded by the configured reconciliation batch size, and feeds the existing
/// deduplicated <see cref="LibraryWorkHint"/> work path. No live Jellyfin or Arr
/// instance is required.
/// </summary>
public sealed class WebhookResolutionTests : IDisposable
{
    private static readonly Guid LibraryId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.UnixEpoch.AddDays(10);

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly MetadataStateStore _store;

    public WebhookResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-webhook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new MetadataStateStore(_repository);
    }

    [Fact]
    public void ResolvesKnownJellyfinItemForTheAdvertisedRadarrRecord()
    {
        var itemId = Guid.NewGuid();
        _store.Write(RadarrEntry(itemId, movieId: 42, movieFileId: 84));
        var resolver = new WebhookReconciliationResolver(_store);

        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42),
            RadarrConfiguration().Current);

        Assert.Equal(new[] { itemId }, resolved);
    }

    [Fact]
    public void AdvertisedRecordWithNoKnownAssociationProducesNoHint()
    {
        _store.Write(RadarrEntry(Guid.NewGuid(), movieId: 42, movieFileId: 84));
        var resolver = new WebhookReconciliationResolver(_store);

        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 999),
            RadarrConfiguration().Current);

        Assert.Empty(resolved);
    }

    [Fact]
    public void DisabledConnectionProducesNoHint()
    {
        _store.Write(RadarrEntry(Guid.NewGuid(), movieId: 42, movieFileId: 84));
        var resolver = new WebhookReconciliationResolver(_store);

        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42),
            RadarrConfiguration(radarrEnabled: false).Current);

        Assert.Empty(resolved);
    }

    [Fact]
    public void ProviderKindMismatchProducesNoHint()
    {
        _store.Write(RadarrEntry(Guid.NewGuid(), movieId: 42, movieFileId: 84));
        var resolver = new WebhookReconciliationResolver(_store);

        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Sonarr, WebhookEventType.Download, seriesId: 42),
            RadarrConfiguration().Current);

        Assert.Empty(resolved);
    }

    [Fact]
    public void UnsupportedEventProducesNoHint()
    {
        _store.Write(RadarrEntry(Guid.NewGuid(), movieId: 42, movieFileId: 84));
        var resolver = new WebhookReconciliationResolver(_store);

        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Unsupported, movieId: 42),
            RadarrConfiguration().Current);

        Assert.Empty(resolved);
    }

    [Fact]
    public void SeriesEventResolvesAllKnownEpisodesOfTheSeries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _store.Write(SonarrEpisodeEntry(first, seriesId: 7, episodeId: 101, episodeFileId: 1001));
        _store.Write(SonarrEpisodeEntry(second, seriesId: 7, episodeId: 102, episodeFileId: 1002));
        _store.Write(SonarrEpisodeEntry(Guid.NewGuid(), seriesId: 8, episodeId: 201, episodeFileId: 2001));
        var resolver = new WebhookReconciliationResolver(_store);

        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Sonarr, WebhookEventType.Rename, seriesId: 7),
            SonarrConfiguration().Current);

        Assert.Equal(2, resolved.Count);
        Assert.Contains(first, resolved);
        Assert.Contains(second, resolved);
    }

    [Fact]
    public void EpisodeEventResolvesOnlyTheAdvertisedEpisode()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _store.Write(SonarrEpisodeEntry(first, seriesId: 7, episodeId: 101, episodeFileId: 1001));
        _store.Write(SonarrEpisodeEntry(second, seriesId: 7, episodeId: 102, episodeFileId: 1002));
        var resolver = new WebhookReconciliationResolver(_store);

        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Sonarr, WebhookEventType.Download, seriesId: 7, episodeIds: new[] { 101 }),
            SonarrConfiguration().Current);

        Assert.Equal(new[] { first }, resolved);
    }

    [Fact]
    public void ResolutionIsBoundedByTheConfiguredReconciliationBatchSize()
    {
        for (var index = 0; index < 3; index++)
        {
            _store.Write(RadarrEntry(Guid.NewGuid(), movieId: 42, movieFileId: 84 + index));
        }

        var resolver = new WebhookReconciliationResolver(_store);
        var resolved = resolver.Resolve(
            new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42),
            RadarrConfiguration(batchSize: 2).Current);

        Assert.Equal(2, resolved.Count);
    }

    [Fact]
    public async Task ServiceEnqueuesTheExistingBoundedWorkHintForResolvedItems()
    {
        var itemId = Guid.NewGuid();
        _store.Write(RadarrEntry(itemId, movieId: 42, movieFileId: 84));
        var configuration = RadarrConfiguration();
        var sink = new RecordingWorkHintSink();
        using var intake = new WebhookIntake(8);
        using var service = new WebhookIntakeService(
            intake,
            new WebhookReconciliationResolver(_store),
            sink,
            configuration);

        await service.StartAsync(CancellationToken.None);
        Assert.True(intake.TrySubmit(new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42)));

        await WaitUntilAsync(() => sink.Count == 1);
        var hint = Assert.Single(sink.Hints);
        Assert.Equal(itemId, hint.ItemId);
        Assert.Equal(LibraryWorkReason.Updated, hint.Reason);
        Assert.Equal(configuration.Current.ConfigurationVersion, hint.ConfigurationVersion);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ReplayedAndDistinctEventsCoalesceIntoTheSameDeduplicatedQueueWork()
    {
        var itemId = Guid.NewGuid();
        _store.Write(RadarrEntry(itemId, movieId: 42, movieFileId: 84));
        var configuration = RadarrConfiguration();
        using var queue = new LibraryWorkQueue(8);
        using var intake = new WebhookIntake(8);
        using var service = new WebhookIntakeService(
            intake,
            new WebhookReconciliationResolver(_store),
            queue,
            configuration);

        await service.StartAsync(CancellationToken.None);

        var download = new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42);
        Assert.True(intake.TrySubmit(download));
        Assert.False(intake.TrySubmit(download)); // replayed delivery is coalesced at the intake.
        await WaitUntilAsync(() => queue.Count == 1);

        // A distinct event that resolves to the same item coalesces into the same
        // pending work item rather than creating a duplicate.
        Assert.True(intake.TrySubmit(new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Rename, movieId: 42)));
        await WaitUntilAsync(() => queue.Count == 1);
        await Task.Delay(50);

        Assert.Equal(1, queue.Count);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ServiceInvalidatesTheEventConnectionInventoryScopedToThatConnection()
    {
        var configuration = CreateConfiguration(radarrEnabled: true, sonarrEnabled: true, batchSize: 100);
        var inventory = new ArrInventoryCacheProvider(configuration);
        var connections = ArrConnectionCatalog.FromSnapshot(configuration.Current);
        var radarr = connections.Single(connection => connection.Provider.Kind == ArrProviderKind.Radarr);
        var sonarr = connections.Single(connection => connection.Provider.Kind == ArrProviderKind.Sonarr);
        var now = DateTimeOffset.UtcNow;

        Assert.True(inventory.Current.TryStore(radarr, now, Array.Empty<ArrInventoryRecordObservation>()));
        Assert.True(inventory.Current.TryStore(sonarr, now, Array.Empty<ArrInventoryRecordObservation>()));
        Assert.Equal(2, inventory.Current.Count);

        using var intake = new WebhookIntake(8);
        using var service = new WebhookIntakeService(
            intake,
            new WebhookReconciliationResolver(_store),
            new RecordingWorkHintSink(),
            configuration,
            inventory: inventory);

        await service.StartAsync(CancellationToken.None);
        Assert.True(intake.TrySubmit(new WebhookEvent(ArrProviderKind.Radarr, WebhookEventType.Download, movieId: 42)));

        // An accepted event invalidates its own connection's retained set (even
        // when it resolves to no known item) and leaves the other connection's set
        // intact, so the invalidation is bounded to the event's scope.
        await WaitUntilAsync(() => inventory.Current.Count == 1);
        Assert.False(inventory.Current.TryGet(radarr.ConnectionId, now, out _));
        Assert.True(inventory.Current.TryGet(sonarr.ConnectionId, now, out _));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ServiceStopIsBoundedAndDoesNotThrow()
    {
        var configuration = RadarrConfiguration();
        using var intake = new WebhookIntake(4);
        using var service = new WebhookIntakeService(
            intake,
            new WebhookReconciliationResolver(_store),
            new RecordingWorkHintSink(),
            configuration);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.False(intake.IsAccepting);
    }

    private static ConfigurationSnapshotService RadarrConfiguration(bool radarrEnabled = true, int batchSize = 100)
    {
        return CreateConfiguration(radarrEnabled, sonarrEnabled: false, batchSize);
    }

    private static ConfigurationSnapshotService SonarrConfiguration(bool sonarrEnabled = true, int batchSize = 100)
    {
        return CreateConfiguration(radarrEnabled: false, sonarrEnabled, batchSize);
    }

    private static ConfigurationSnapshotService CreateConfiguration(bool radarrEnabled, bool sonarrEnabled, int batchSize)
    {
        var configuration = new PluginConfiguration
        {
            WebhookSecret = "webhook-secret",
            Limits = new OperationalLimits { ReconciliationBatchSize = batchSize },
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = radarrEnabled,
                BaseUrl = "http://radarr.test",
                ApiKey = "radarr-key",
            },
            Sonarr = new ArrConnectionConfiguration
            {
                Enabled = sonarrEnabled,
                BaseUrl = "http://sonarr.test",
                ApiKey = "sonarr-key",
            },
        };

        return new ConfigurationSnapshotService(configuration);
    }

    private static MetadataStateEntry RadarrEntry(Guid itemId, int movieId, int movieFileId)
    {
        var identity = new MediaIdentity(
            itemId,
            MediaItemType.Movie,
            LibraryId,
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" },
            "Example Movie",
            2021,
            sourceFingerprint: "SOURCE-FINGERPRINT");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Radarr, "http://radarr.test");
        var provider = new ArrProvider(ArrProviderKind.Radarr, connectionId.Value);
        var record = new RadarrIdentity(connectionId, movieId, ArrFileIdentity.Present(movieFileId));
        var match = new MediaMatch(identity, provider, connectionId, MediaMatchStatus.Matched, MediaMatchMethod.ProviderId, record);
        var metadata = new BadgeMetadata(provider, record, ObservedAt, videoCodec: "h264");

        return MetadataStateEntry.From(identity, match, metadata, ObservedAt);
    }

    private static MetadataStateEntry SonarrEpisodeEntry(Guid itemId, int seriesId, int episodeId, int episodeFileId)
    {
        var identity = new MediaIdentity(
            itemId,
            MediaItemType.Episode,
            LibraryId,
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "999001" },
            "Example Episode",
            seriesIdentity: null,
            seasonNumber: 2,
            episodeNumber: 5,
            sourceFingerprint: "SOURCE-FINGERPRINT");
        var connectionId = ArrConnectionId.For(ArrProviderKind.Sonarr, "http://sonarr.test");
        var provider = new ArrProvider(ArrProviderKind.Sonarr, connectionId.Value);
        var record = new SonarrIdentity(connectionId, seriesId, episodeId, ArrFileIdentity.Present(episodeFileId));
        var match = new MediaMatch(identity, provider, connectionId, MediaMatchStatus.Matched, MediaMatchMethod.ProviderId, record);
        var metadata = new BadgeMetadata(provider, record, ObservedAt, videoCodec: "h264");

        return MetadataStateEntry.From(identity, match, metadata, ObservedAt);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
