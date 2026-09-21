using System;
using System.IO;
using System.Linq;
using System.Text;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 6.3 canonical metadata state record and its
/// versioned cache persistence: round-trip, in-place replacement on a changed
/// fingerprint, corrupt and semantically invalid discard, reload, and
/// secret-free persistence. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class MetadataStateStoreTests : IDisposable
{
    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly MetadataStateStore _store;

    public MetadataStateStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-metadata-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new MetadataStateStore(_repository);
    }

    [Fact]
    public void WriteAndReadRoundTripsAMatchedEntryThroughTheCacheBoundary()
    {
        var entry = ReconciliationFixtures.MatchedEntry();

        _store.Write(entry);
        var result = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);

        Assert.Equal(StateReadStatus.Found, result.Status);
        var stored = result.Value!;
        Assert.Equal(MetadataStateEntry.CurrentCacheVersion, stored.CacheVersion);
        Assert.Equal(entry.CacheKey, stored.CacheKey);
        Assert.Equal(ReconciliationFixtures.ItemId, stored.JellyfinItemId);
        Assert.Equal(MediaItemType.Movie, stored.ItemType);
        Assert.Equal(ArrProviderKind.Radarr, stored.ProviderKind);
        Assert.Equal(ArrProviderKind.Radarr.ToApiName(), stored.ProviderKind.ToApiName());
        Assert.Equal(MediaMatchStatus.Matched, stored.MatchStatus);
        Assert.Equal(MediaMatchMethod.ProviderId, stored.MatchMethod);
        Assert.Equal(MetadataStateKind.Fresh, stored.State);
        Assert.Equal(entry.MetadataFingerprint, stored.MetadataFingerprint);
        Assert.False(string.IsNullOrEmpty(stored.MetadataFingerprint));
        Assert.NotNull(stored.RecordIdentity);
        Assert.Equal(42, stored.RecordIdentity!.MovieId);
        Assert.Equal(ArrFilePresence.Present, stored.RecordIdentity.MovieFilePresence);
        Assert.Equal(84, stored.RecordIdentity.MovieFileId);
        Assert.NotNull(stored.Metadata);
        Assert.Equal("Bluray-1080p", stored.Metadata!.QualityLabel);
        Assert.Equal("h264", stored.Metadata.VideoCodec);
        Assert.Equal("bluray", stored.Metadata.Source);
        Assert.True(stored.Metadata.UpgradePending);
        Assert.NotNull(stored.Metadata.AudioFeatures);
        Assert.Single(stored.Metadata.AudioFeatures!);
        Assert.Equal(ArrAudioFeature.Atmos, stored.Metadata.AudioFeatures![0]);
        Assert.Equal("603", stored.ProviderIds["tmdb"]);
    }

    [Fact]
    public void UnknownAndEmptyAudioFeaturesRoundTripDistinctly()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);

        var unknown = MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity, unknownAudioFeatures: true),
            ReconciliationFixtures.ObservedAt);
        var empty = MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity, includeAudioFeatures: false),
            ReconciliationFixtures.ObservedAt);

        Assert.Null(unknown.Metadata!.AudioFeatures);
        Assert.NotNull(empty.Metadata!.AudioFeatures);
        Assert.Empty(empty.Metadata.AudioFeatures!);
        Assert.NotEqual(unknown.MetadataFingerprint, empty.MetadataFingerprint);

        _store.Write(unknown);
        var storedUnknown = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Value!;
        Assert.Null(storedUnknown.Metadata!.AudioFeatures);

        _store.Write(empty);
        var storedEmpty = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Value!;
        Assert.NotNull(storedEmpty.Metadata!.AudioFeatures);
        Assert.Empty(storedEmpty.Metadata.AudioFeatures!);
    }

    [Fact]
    public void UnmatchedEntryRoundTripsWithoutMetadata()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = new MediaMatch(
            identity,
            ReconciliationFixtures.RadarrProvider,
            ReconciliationFixtures.RadarrConnectionId,
            MediaMatchStatus.NotFound,
            MediaMatchMethod.None,
            ambiguityReason: "No candidate matched.");

        var entry = MetadataStateEntry.From(identity, match, null, ReconciliationFixtures.ObservedAt);
        Assert.Equal(MetadataStateKind.Unmatched, entry.State);

        _store.Write(entry);
        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Value!;

        Assert.Equal(MetadataStateKind.Unmatched, stored.State);
        Assert.Null(stored.Metadata);
        Assert.Null(stored.MetadataFingerprint);
        Assert.Null(stored.RecordIdentity);
    }

    [Fact]
    public void ChangedFingerprintReplacesTheSnapshotInPlace()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        var original = MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            ReconciliationFixtures.ObservedAt);
        var replacement = MetadataStateEntry.From(
            identity,
            match,
            new ArrTags.Metadata.BadgeMetadata(
                match.Provider,
                match.RecordIdentity!,
                ReconciliationFixtures.ObservedAt,
                quality: new ArrTags.Metadata.ArrQualityDescriptor("Bluray-2160p", "bluray", 2160, "Remux", 8)),
            ReconciliationFixtures.ObservedAt.AddMinutes(5));

        Assert.NotEqual(original.MetadataFingerprint, replacement.MetadataFingerprint);
        Assert.Equal(
            MetadataStateStore.GetRecordId(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr),
            MetadataStateStore.GetRecordId(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr));

        _store.Write(original);
        _store.Write(replacement);
        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Value!;

        Assert.Equal(replacement.MetadataFingerprint, stored.MetadataFingerprint);
        Assert.Equal("Bluray-2160p", stored.Metadata!.QualityLabel);
        Assert.Single(Directory.GetFiles(
            _repository.Paths.GetKindDirectory(StateAuthority.Cache, MetadataStateStore.RecordKind),
            "*.json"));
    }

    [Fact]
    public void CorruptCacheEntryIsDiscardedAndRebuildable()
    {
        _store.Write(ReconciliationFixtures.MatchedEntry());
        var recordId = MetadataStateStore.GetRecordId(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, MetadataStateStore.RecordKind, recordId);
        File.WriteAllText(path, "{ this is not valid json");

        var result = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);

        Assert.Equal(StateReadStatus.InvalidDiscarded, result.Status);
        Assert.False(File.Exists(path));

        // The record can be rebuilt by a later observation.
        _store.Write(ReconciliationFixtures.MatchedEntry());
        Assert.Equal(StateReadStatus.Found, _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public void SemanticallyInvalidCacheEntryIsDiscarded()
    {
        var recordId = MetadataStateStore.GetRecordId(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        var invalid = new MetadataStateEntry(
            MetadataStateEntry.CurrentCacheVersion,
            "cache-key",
            ReconciliationFixtures.ItemId,
            MediaItemType.Movie,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            "match-fingerprint",
            ArrProviderKind.Radarr,
            ReconciliationFixtures.RadarrConnectionId.Value,
            ReconciliationFixtures.RadarrConnectionId.Value,
            MetadataStateKind.Fresh);
        Assert.False(invalid.Validate(out _));

        var bytes = StateEnvelopeCodec.Serialize(
            StateAuthority.Cache,
            MetadataStateStore.RecordKind,
            recordId,
            invalid,
            DateTimeOffset.UtcNow,
            terminal: false);
        var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, MetadataStateStore.RecordKind, recordId);
        AtomicFileWriter.Write(path, bytes);

        var result = _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);

        Assert.Equal(StateReadStatus.InvalidDiscarded, result.Status);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void WriteRejectsAnEntryThatViolatesTheDocumentedInvariants()
    {
        var invalid = new MetadataStateEntry(
            MetadataStateEntry.CurrentCacheVersion,
            "cache-key",
            ReconciliationFixtures.ItemId,
            MediaItemType.Movie,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            "match-fingerprint",
            ArrProviderKind.Radarr,
            ReconciliationFixtures.RadarrConnectionId.Value,
            ReconciliationFixtures.RadarrConnectionId.Value,
            MetadataStateKind.Fresh);

        Assert.Throws<ArgumentException>(() => _store.Write(invalid));
    }

    [Fact]
    public void PersistenceReloadRoundTripsThroughAFreshStore()
    {
        var entry = ReconciliationFixtures.MatchedEntry();
        _store.Write(entry);

        var reloaded = new MetadataStateStore(new StateRepository(_root));
        var result = reloaded.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);

        Assert.Equal(StateReadStatus.Found, result.Status);
        Assert.Equal(entry.MetadataFingerprint, result.Value!.MetadataFingerprint);
        Assert.Equal(entry.CacheKey, result.Value.CacheKey);
        Assert.Equal(entry.MatchFingerprint, result.Value.MatchFingerprint);
    }

    [Fact]
    public void PersistedStateContainsNoCredentials()
    {
        const string apiKey = "super-secret-radarr-key";
        var configuration = new PluginConfiguration
        {
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = apiKey,
            },
        };
        var snapshot = PluginConfigurationSnapshot.From(configuration, 1);
        var connection = ArrConnectionCatalog.FromSnapshot(snapshot)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Radarr);

        var identity = ReconciliationFixtures.MovieIdentity();
        var record = new RadarrIdentity(connection.ConnectionId, 42, ArrFileIdentity.Present(84));
        var match = new MediaMatch(
            identity,
            connection.Provider,
            connection.ConnectionId,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            record);
        var metadata = new ArrTags.Metadata.BadgeMetadata(
            connection.Provider,
            record,
            ReconciliationFixtures.ObservedAt,
            videoCodec: "h264");
        var entry = MetadataStateEntry.From(identity, match, metadata, ReconciliationFixtures.ObservedAt);

        Assert.DoesNotContain(apiKey, entry.CacheKey, StringComparison.Ordinal);

        _store.Write(entry);
        var recordId = MetadataStateStore.GetRecordId(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);
        var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, MetadataStateStore.RecordKind, recordId);
        var persisted = File.ReadAllText(path, Encoding.UTF8);

        Assert.DoesNotContain(apiKey, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("api-key", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apikey", persisted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordIdIsStableAndPathSafe()
    {
        var recordId = MetadataStateStore.GetRecordId(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr);

        Assert.Equal("11111111222233334444555555555555-radarr", recordId);
        Assert.DoesNotContain("/", recordId, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", recordId, StringComparison.Ordinal);
        Assert.DoesNotContain(":", recordId, StringComparison.Ordinal);
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
