using System;
using System.IO;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Reconciliation;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 6.5 metadata freshness policy: the explicit
/// fresh/stale/expired transitions, the computed <c>expiresAt</c>/<c>staleUntil</c>
/// boundaries derived from the ADR-004 window, and the separation of metadata
/// freshness retention from the render work-cache eviction policy. No live
/// Jellyfin or Arr instance is required.
/// </summary>
public sealed class MetadataFreshnessTests : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly MetadataStateStore _store;

    public MetadataFreshnessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-metadata-freshness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new MetadataStateStore(_repository);
    }

    [Fact]
    public void SuccessfulObservationIsFreshBeforeItsFreshnessBoundary()
    {
        var entry = FreshEntry();

        Assert.Equal(MetadataFreshness.Fresh, entry.EvaluateFreshness(entry.FetchedAt!.Value));
        Assert.True(entry.IsUsableAsCurrent(entry.FetchedAt!.Value));
        Assert.False(entry.IsExpired(entry.FetchedAt!.Value));
    }

    [Fact]
    public void ObservationBecomesStaleAfterItsFreshnessBoundaryButIsUsableUntilTheStaleBoundary()
    {
        var entry = FreshEntry();
        var justAfterExpiry = entry.ExpiresAt!.Value.AddMinutes(1);
        var justBeforeStale = entry.StaleUntil!.Value.AddMinutes(-1);

        Assert.Equal(MetadataFreshness.Stale, entry.EvaluateFreshness(justAfterExpiry));
        Assert.Equal(MetadataFreshness.Stale, entry.EvaluateFreshness(justBeforeStale));
        Assert.True(entry.IsUsableAsCurrent(justAfterExpiry));
        Assert.True(entry.IsUsableAsCurrent(justBeforeStale));
    }

    [Fact]
    public void ObservationIsExpiredAndNotUsableAtOrAfterTheStaleBoundary()
    {
        var entry = FreshEntry();

        Assert.Equal(MetadataFreshness.Expired, entry.EvaluateFreshness(entry.StaleUntil!.Value));
        Assert.Equal(MetadataFreshness.Expired, entry.EvaluateFreshness(entry.StaleUntil!.Value.AddDays(1)));
        Assert.False(entry.IsUsableAsCurrent(entry.StaleUntil!.Value));
        Assert.True(entry.IsExpired(entry.StaleUntil!.Value));
    }

    [Fact]
    public void TimestampsAreDerivedFromTheConfiguredWindowAndTotalEqualsThatWindow()
    {
        var fetchedAt = DateTimeOffset.UtcNow;

        var entry = MetadataStateEntry.From(
            ReconciliationFixtures.MovieIdentity(),
            ReconciliationFixtures.MatchedMovieMatch(ReconciliationFixtures.MovieIdentity()),
            ReconciliationFixtures.MovieMetadata(ReconciliationFixtures.MovieIdentity()),
            fetchedAt,
            staleWindow: Window);

        // The configured window is the total bounded last-known-good lifetime:
        // fresh for the first half, bounded stale for the remaining half.
        Assert.Equal(fetchedAt + (Window / 2), entry.ExpiresAt);
        Assert.Equal(fetchedAt + Window, entry.StaleUntil);
        Assert.Equal(Window, entry.StaleUntil!.Value - fetchedAt);
        Assert.Equal(Window / 2, entry.ExpiresAt!.Value - fetchedAt);

        // Fresh strictly before the half boundary; stale but usable from the
        // half boundary up to (not including) the full boundary; expired at and
        // after the full boundary.
        Assert.Equal(MetadataFreshness.Fresh, entry.EvaluateFreshness(entry.ExpiresAt!.Value.AddTicks(-1)));
        Assert.Equal(MetadataFreshness.Stale, entry.EvaluateFreshness(entry.ExpiresAt!.Value));
        Assert.Equal(MetadataFreshness.Stale, entry.EvaluateFreshness(entry.StaleUntil!.Value.AddTicks(-1)));
        Assert.True(entry.IsUsableAsCurrent(entry.StaleUntil!.Value.AddTicks(-1)));
        Assert.Equal(MetadataFreshness.Expired, entry.EvaluateFreshness(entry.StaleUntil!.Value));
        Assert.False(entry.IsUsableAsCurrent(entry.StaleUntil!.Value));
    }

    [Fact]
    public void DefaultWindowIsTheAdr004Default()
    {
        var fetchedAt = DateTimeOffset.UtcNow;

        var entry = MetadataStateEntry.From(
            ReconciliationFixtures.MovieIdentity(),
            ReconciliationFixtures.MatchedMovieMatch(ReconciliationFixtures.MovieIdentity()),
            ReconciliationFixtures.MovieMetadata(ReconciliationFixtures.MovieIdentity()),
            fetchedAt);

        Assert.Equal(
            TimeSpan.FromMinutes(OperationalLimits.DefaultMetadataStaleWindowMinutes),
            MetadataStateEntry.DefaultStaleWindow);
        Assert.Equal(fetchedAt + (MetadataStateEntry.DefaultStaleWindow / 2), entry.ExpiresAt);
        Assert.Equal(fetchedAt + MetadataStateEntry.DefaultStaleWindow, entry.StaleUntil);
        Assert.Equal(MetadataStateEntry.DefaultStaleWindow, entry.StaleUntil!.Value - fetchedAt);
    }

    [Fact]
    public void StaleConversionKeepsTheBoundedTimestampsAndSnapshot()
    {
        var entry = FreshEntry();

        var stale = entry.ToStale();

        Assert.Equal(MetadataStateKind.Stale, stale.State);
        Assert.Equal(entry.FetchedAt, stale.FetchedAt);
        Assert.Equal(entry.ExpiresAt, stale.ExpiresAt);
        Assert.Equal(entry.StaleUntil, stale.StaleUntil);
        Assert.Equal(entry.MetadataFingerprint, stale.MetadataFingerprint);
        Assert.Same(stale, stale.ToStale());
        Assert.True(stale.IsUsableAsCurrent(entry.ExpiresAt!.Value.AddMinutes(1)));
        Assert.False(stale.IsUsableAsCurrent(entry.StaleUntil!.Value));
    }

    [Fact]
    public void UnmatchedRecordIsNotUsableAndExpiresAfterTheWindow()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = new ArrTags.Matching.MediaMatch(
            identity,
            ReconciliationFixtures.RadarrProvider,
            ReconciliationFixtures.RadarrConnectionId,
            ArrTags.Matching.MediaMatchStatus.NotFound,
            ArrTags.Matching.MediaMatchMethod.None,
            ambiguityReason: "No candidate matched.");
        var entry = MetadataStateEntry.From(identity, match, null, ReconciliationFixtures.ObservedAt, staleWindow: Window);

        Assert.Equal(MetadataFreshness.Unmatched, entry.EvaluateFreshness(entry.ExpiresAt!.Value));
        Assert.False(entry.IsUsableAsCurrent(entry.ExpiresAt!.Value));
        Assert.Equal(MetadataFreshness.Expired, entry.EvaluateFreshness(entry.StaleUntil!.Value));
    }

    [Fact]
    public void FreshRecordWithoutComputedBoundariesIsInvalid()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        var metadata = ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity);

        var entry = new MetadataStateEntry(
            MetadataStateEntry.CurrentCacheVersion,
            "cache-key",
            identity.JellyfinItemId,
            identity.ItemType,
            match.Status,
            match.MatchMethod,
            match.MatchFingerprint,
            match.Provider.Kind,
            match.Provider.ProviderInstanceId,
            match.ConnectionId.Value,
            MetadataStateKind.Fresh,
            metadata: MetadataSnapshot.From(metadata),
            metadataFingerprint: metadata.MetadataFingerprint);

        Assert.False(entry.Validate(out var reason));
        Assert.Contains("freshness boundaries", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => _store.Write(entry));
    }

    [Fact]
    public void RetentionPrunesOnlyExpiredMetadataRecords()
    {
        _store.Write(FreshEntryAt(DateTimeOffset.UtcNow));

        Assert.Equal(0, _store.ApplyRetention(DateTimeOffset.UtcNow.AddMinutes(1)));

        var stored = _store.Read(ReconciliationFixtures.ItemId, ArrTags.Providers.ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, stored.Status);
        Assert.True(stored.Value!.IsUsableAsCurrent(DateTimeOffset.UtcNow));

        var removed = _store.ApplyRetention(DateTimeOffset.UtcNow.AddDays(3));

        Assert.Equal(1, removed);
        Assert.Equal(
            StateReadStatus.Missing,
            _store.Read(ReconciliationFixtures.ItemId, ArrTags.Providers.ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public void RepositoryCacheRetentionExemptsMetadataState()
    {
        var limits = new OperationalLimits { RenderCacheTtlMinutes = 1 };
        var repository = new StateRepository(_root, limits);
        var store = new MetadataStateStore(repository);
        store.Write(FreshEntry());

        var recordId = MetadataStateStore.GetRecordId(
            ReconciliationFixtures.ItemId,
            ArrTags.Providers.ArrProviderKind.Radarr);
        var path = repository.Paths.GetRecordPath(StateAuthority.Cache, MetadataStateStore.RecordKind, recordId);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        var removed = repository.ApplyRetention(
            DateTimeOffset.UtcNow,
            new[] { MetadataStateStore.RecordKind });

        Assert.Equal(0, removed);
        Assert.Equal(StateReadStatus.Found, store.Read(recordId).Status);
    }

    private static MetadataStateEntry FreshEntry() => FreshEntryAt(ReconciliationFixtures.ObservedAt);

    private static MetadataStateEntry FreshEntryAt(DateTimeOffset fetchedAt)
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        return MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            fetchedAt,
            staleWindow: Window);
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
