using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using ArrTags.Reconciliation;
using ArrTags.State;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Goal C integration checks for the provider inventory cache at the
/// provider-client boundary (ADR-018 clauses 2, 4, and 5): one library read per
/// connection serves multiple work items in a reconciliation window (including
/// concurrent cold readers), the bulk selection endpoints are used instead of
/// per-item file reads and are chunked into bounded requests, an over-bound
/// observation set keeps the direct read, a provider library read failure fails
/// the window while the per-item bounded last-known-good metadata state is
/// retained, a sub-read failure falls back to the unchanged direct read, and the
/// cached observations stay canonical and secret-free. These tests require no
/// live Jellyfin or Arr instance.
/// </summary>
public sealed class ProviderInventoryCacheIntegrationTests
{
    private const string ApiKey = "inventory-super-secret";

    /// <summary>
    /// The complete set of provider endpoints ArrTags is permitted to call for
    /// reconciliation reads. A revision-token poll, a <c>history/since</c>
    /// watermark, the <c>?h=</c> cacheable bypass, or any other unlisted path
    /// fails the negative-dependency test by not being a member of this set.
    /// </summary>
    private static readonly string[] AllowedProviderEndpointPaths =
    {
        "/api/v3/system/status",
        "/api/v3/movie",
        "/api/v3/moviefile",
        "/api/v3/series",
        "/api/v3/episode",
        "/api/v3/episodeFile",
    };

    /// <summary>
    /// The complete set of query-parameter names the documented reconciliation
    /// endpoints may carry. A cacheable-bypass or history watermark parameter
    /// fails by not being a member of this set.
    /// </summary>
    private static readonly HashSet<string> AllowedProviderQueryKeys = new(StringComparer.Ordinal)
    {
        "movieId",
        "seriesId",
        "includeEpisodeFile",
        "episodeFileIds",
    };

    [Fact]
    public async Task RadarrLibraryReadServesMultipleDistinctWorkItemsWithinTheWindow()
    {
        var (configuration, radarr, _) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        // Two distinct Jellyfin movies are matched from one population.
        var first = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);
        var second = await reader.ReadAsync(MovieIdentity(604), radarr, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(MediaMatchStatus.Matched, second.Match!.Status);
        Assert.Equal("Bluray-1080p", first.Metadata!.Quality!.Label);
        Assert.Equal("Bluray-1080p", second.Metadata!.Quality!.Label);

        // One library read serves both work items: the second read is served
        // entirely from the cache with no provider request.
        Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, inventory.Current.Count);

        // The whole library is retained, not just the first matched record.
        Assert.True(inventory.Current.TryGet(radarr.ConnectionId, DateTimeOffset.UtcNow, out var entry));
        Assert.Equal(2, entry!.Records.Count);
    }

    [Fact]
    public async Task RadarrBulkMovieFileReadUsesRepeatedMovieIdsInsteadOfPerItemReads()
    {
        var (configuration, radarr, _) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var (reader, _) = BuildRadarrReader(configuration, handler);

        await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        // Both movies' fully populated files are read in one request through the
        // repeatable movieId selector; no per-item movie-file read is issued.
        var bulk = Assert.Single(RequestsFor(handler, "/api/v3/moviefile"));
        Assert.Equal("/api/v3/moviefile?movieId=7&movieId=8", bulk.PathAndQuery);
        Assert.DoesNotContain(handler.Requests, uri => uri.PathAndQuery == "/api/v3/moviefile?movieId=7");
        Assert.DoesNotContain(handler.Requests, uri => uri.PathAndQuery == "/api/v3/moviefile?movieId=8");
    }

    [Fact]
    public async Task RadarrCacheHitAvoidsAnyProviderCall()
    {
        var (configuration, radarr, _) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var (reader, _) = BuildRadarrReader(configuration, handler);

        await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);
        handler.Clear();

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Metadata);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RadarrOverBoundObservationSetFallsBackToTheDirectRead()
    {
        var (configuration, radarr, _) = BuildConfiguration(limits => limits.InventoryCacheMaxRecords = 1);
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bluray-1080p", result.Metadata!.Quality!.Label);

        // The over-bound set is not cached and the direct read is used unchanged:
        // one library read and the per-record movie-file read, no bulk read.
        Assert.Equal(0, inventory.Current.Count);
        Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));
        Assert.Single(RequestsFor(handler, "/api/v3/moviefile"));
        Assert.Equal("/api/v3/moviefile?movieId=7", handler.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task RadarrBulkFileReadFailureFallsBackToTheDirectReadInsteadOfFailingTheWindow()
    {
        // The library read succeeds but the population's bulk movie-file read
        // fails. The bulk read is an optimization, so the reader falls back to
        // the unchanged direct read (reusing the library read it already made
        // and reading the matched movie's file per record) rather than failing
        // the window; the observation set is not cached.
        var (configuration, radarr, _) = BuildConfiguration(limits => limits.TransientRetryCount = 0);
        var handler = new RecordingHandler(RadarrBulkFileFailureResponder);
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bluray-1080p", result.Metadata!.Quality!.Label);
        Assert.Equal(0, inventory.Current.Count);
        Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));

        // One failed bulk request plus the per-record direct read; the direct
        // read never issues the bulk (multi-id) form.
        Assert.Equal(2, RequestsFor(handler, "/api/v3/moviefile").Count);
        Assert.Equal("/api/v3/moviefile?movieId=7", handler.Requests.Last().PathAndQuery);
    }

    [Fact]
    public async Task RadarrProviderFailureWithNoCacheReturnsTheBoundedFailure()
    {
        var (configuration, radarr, _) = BuildConfiguration(limits => limits.TransientRetryCount = 0);
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: true));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, result.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Later, result.Error.Retryability);
        Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));
        Assert.Equal(0, inventory.Current.Count);
    }

    [Fact]
    public async Task RadarrLibraryReadFailureOnACacheMissFailsTheWindowAndKeepsBoundedLastKnownGoodMetadata()
    {
        // The library read (`/api/v3/movie`) is the population's first read, so
        // a failure there fails the whole window: the reader returns a bounded
        // failure, caches nothing, and every work item in the window fails and
        // re-attempts the population. Compose the real reader and bounded cache
        // with the real reconciliation processor and durable metadata store to
        // pin the end-to-end behavior, including the existing per-item bounded
        // last-known-good metadata state.
        var root = Path.Combine(Path.GetTempPath(), "arrtags-inventory-window-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fail = false;
            var (configuration, radarr, _) = BuildConfiguration(limits => limits.TransientRetryCount = 0);
            var handler = new RecordingHandler(uri => RadarrResponder(uri, fail));
            var inventory = new ArrInventoryCacheProvider(configuration);
            var reader = new RadarrMetadataReader(BuildFactory(configuration, handler), log: null, inventory: inventory);

            var resolver = new ReconciliationLibraryResolver();
            resolver.LibraryIds[ReconciliationFixtures.ItemId] = ReconciliationFixtures.LibraryId;
            resolver.Items[ReconciliationFixtures.ItemId] =
                ReconciliationFixtures.Movie(ReconciliationFixtures.ItemId, ReconciliationFixtures.LibraryId);

            var store = new MetadataStateStore(new StateRepository(root, configuration.Current.Limits));
            var processor = new MetadataReconciliationProcessor(
                configuration,
                resolver,
                new IArrMetadataReader[] { reader },
                store);
            var workItem = new LibraryWorkItem(
                new WorkItemKey(ReconciliationFixtures.ItemId, null, ArtworkImageSurface.Primary),
                LibraryWorkReason.Updated,
                configuration.Current.ConfigurationVersion);

            // First run: a cache miss performs one library read plus the bulk
            // file read, caches the whole library, and publishes a fresh
            // per-item metadata state.
            var published = await processor.ProcessAsync(workItem, CancellationToken.None);

            Assert.True(published.IsSuccess, published.Reason);
            Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));
            Assert.Equal(1, inventory.Current.Count);
            var fresh = store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Value!;
            Assert.Equal(MetadataStateKind.Fresh, fresh.State);

            // The window is invalidated and the library read then fails: the
            // whole window fails and caches nothing.
            inventory.Invalidate(radarr.ConnectionId);
            fail = true;

            var failed = await processor.ProcessAsync(workItem, CancellationToken.None);

            Assert.False(failed.IsSuccess);
            Assert.True(failed.IsRetryable);
            Assert.Equal(2, CountRequests(handler, "/api/v3/movie"));
            Assert.Equal(0, inventory.Current.Count);
            Assert.False(inventory.Current.TryGet(radarr.ConnectionId, DateTimeOffset.UtcNow, out _));

            // The existing per-item bounded last-known-good metadata state is
            // retained as explicit stale with an unchanged fingerprint and
            // bounded window; it is not a stale inventory snapshot.
            var kept = store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Value!;
            Assert.Equal(MetadataStateKind.Stale, kept.State);
            Assert.Equal(fresh.MetadataFingerprint, kept.MetadataFingerprint);
            Assert.Equal(fresh.ExpiresAt, kept.ExpiresAt);
            Assert.Equal(fresh.StaleUntil, kept.StaleUntil);
            Assert.True(kept.IsUsableAsCurrent(DateTimeOffset.UtcNow));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RadarrStaleObservationSetIsServedWithoutAProviderCallWhenTheProviderFails()
    {
        // A one-minute TTL makes the entry stale after 30 seconds and unusable
        // after 60. The stored set is observed 45 seconds ago, so it is a
        // bounded last-known-good set that is still usable as current.
        var (configuration, radarr, _) = BuildConfiguration(limits => limits.InventoryCacheTtlMinutes = 1);
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: true));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);
        var observedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(45);
        StoreRadarrObservation(inventory.Current, radarr, observedAt, movieId: 7, tmdbId: 603, fileId: 42);

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bluray-1080p", result.Metadata!.Quality!.Label);
        Assert.Empty(handler.Requests);
        Assert.Equal(1, inventory.Current.Count);
    }

    [Fact]
    public async Task RadarrExpiredObservationSetIsEvictedAndTheProviderFailureIsBounded()
    {
        var (configuration, radarr, _) = BuildConfiguration(limits =>
        {
            limits.InventoryCacheTtlMinutes = 1;
            limits.TransientRetryCount = 0;
        });
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: true));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        // Observed 90 seconds ago with a one-minute TTL: past StaleUntil, so the
        // reader must not serve it and must re-read the provider instead.
        var observedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(90);
        StoreRadarrObservation(inventory.Current, radarr, observedAt, movieId: 7, tmdbId: 603, fileId: 42);

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.ProviderUnavailable, result.Error!.Code);
        Assert.Equal(ArrErrorRetryability.Later, result.Error.Retryability);

        // The expired set was evicted, the provider was actually read, and the
        // failure did not retain or extend the bounded window.
        Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));
        Assert.Equal(0, inventory.Current.Count);
    }

    [Fact]
    public async Task SonarrLibraryReadServesMultipleDistinctWorkItemsWithinTheWindow()
    {
        var (configuration, _, sonarr) = BuildConfiguration();
        var handler = new RecordingHandler(uri => SonarrResponder(uri, embeddedFile: true));
        var (reader, inventory) = BuildSonarrReader(configuration, handler);

        // Two distinct episodes of the same series are matched from one
        // population.
        var first = await reader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);
        var second = await reader.ReadAsync(EpisodeIdentity(12345, 9002), sonarr, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(MediaMatchStatus.Matched, second.Match!.Status);
        Assert.Equal("WEBDL-1080p", first.Metadata!.Quality!.Label);
        Assert.Equal("WEBDL-1080p", second.Metadata!.Quality!.Label);

        // One series library read and one per-series episode read serve both work
        // items; the second read is served from the cache with no provider call.
        Assert.Equal(1, CountRequests(handler, "/api/v3/series"));
        Assert.Equal(1, CountRequests(handler, "/api/v3/episode"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, inventory.Current.Count);

        // The whole library (series plus both episodes) is retained.
        Assert.True(inventory.Current.TryGet(sonarr.ConnectionId, DateTimeOffset.UtcNow, out var entry));
        Assert.Equal(3, entry!.Records.Count);

        // No per-item episode-file read is used when the embedded file resolves.
        Assert.Empty(RequestsFor(handler, "/api/v3/episodeFile"));
    }

    [Fact]
    public async Task SonarrBulkEpisodeFileReadUsesRepeatedEpisodeFileIdsInsteadOfPerSeriesReads()
    {
        var (configuration, _, sonarr) = BuildConfiguration();
        var handler = new RecordingHandler(uri => SonarrResponder(uri, embeddedFile: false));
        var (reader, _) = BuildSonarrReader(configuration, handler);

        var result = await reader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("WEBDL-1080p", result.Metadata!.Quality!.Label);

        // The two unresolved embedded files are read through the repeatable
        // episodeFileIds selector in one request, never the per-series inventory.
        var bulk = Assert.Single(RequestsFor(handler, "/api/v3/episodeFile"));
        Assert.Equal("/api/v3/episodeFile?episodeFileIds=418&episodeFileIds=419", bulk.PathAndQuery);
        Assert.DoesNotContain(handler.Requests, uri => uri.PathAndQuery == "/api/v3/episodeFile?seriesId=12");
    }

    [Fact]
    public async Task SonarrCacheHitAvoidsAnyProviderCall()
    {
        var (configuration, _, sonarr) = BuildConfiguration();
        var handler = new RecordingHandler(uri => SonarrResponder(uri, embeddedFile: true));
        var (reader, _) = BuildSonarrReader(configuration, handler);

        await reader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);
        handler.Clear();

        var result = await reader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Metadata);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SonarrOverBoundObservationSetFallsBackToTheDirectRead()
    {
        var (configuration, _, sonarr) = BuildConfiguration(limits => limits.InventoryCacheMaxRecords = 1);
        var handler = new RecordingHandler(uri => SonarrResponder(uri, embeddedFile: true));
        var (reader, inventory) = BuildSonarrReader(configuration, handler);

        var result = await reader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("WEBDL-1080p", result.Metadata!.Quality!.Label);

        // The over-bound set is not cached and the direct read is used unchanged:
        // the population reads the library plus the series episodes, then the
        // direct read reads the matched series' episodes again, and no bulk
        // episode-file read is issued.
        Assert.Equal(0, inventory.Current.Count);
        Assert.Equal(1, CountRequests(handler, "/api/v3/series"));
        Assert.Equal(2, CountRequests(handler, "/api/v3/episode"));
        Assert.Empty(RequestsFor(handler, "/api/v3/episodeFile"));
    }

    [Fact]
    public async Task SonarrEpisodeReadFailureFallsBackToTheDirectReadInsteadOfFailingTheWindow()
    {
        // The series library read succeeds, but the population's per-series
        // episode read for a non-matched series fails. The episode read is part
        // of the inventory, so the population stores nothing, but the reader
        // falls back to the unchanged direct read for the matched series rather
        // than failing the work item.
        var (configuration, _, sonarr) = BuildConfiguration(limits => limits.TransientRetryCount = 0);
        var handler = new RecordingHandler(SonarrEpisodeReadFailureResponder);
        var (reader, inventory) = BuildSonarrReader(configuration, handler);

        var result = await reader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("WEBDL-1080p", result.Metadata!.Quality!.Label);
        Assert.Equal(0, inventory.Current.Count);
        Assert.Equal(1, CountRequests(handler, "/api/v3/series"));

        // The population read series 12 (matched) then series 13 (failed), and
        // the direct fallback read series 12 again; no per-series inventory is
        // retained.
        Assert.Equal(3, CountRequests(handler, "/api/v3/episode"));
        Assert.DoesNotContain(handler.Requests, uri => uri.PathAndQuery == "/api/v3/episodeFile?seriesId=12");
    }

    [Fact]
    public async Task SonarrCachePreservesMatchedMetadataForAnEpisodeWithoutAFile()
    {
        var (configuration, _, sonarr) = BuildConfiguration();
        var handler = new RecordingHandler(uri =>
        {
            if (uri.AbsolutePath.EndsWith("/api/v3/series", StringComparison.Ordinal))
            {
                return Json("""[{"id":12,"title":"Example","tvdbId":12345}]""");
            }

            if (uri.AbsolutePath.EndsWith("/api/v3/episode", StringComparison.Ordinal))
            {
                return Json("""[{"id":73,"seriesId":12,"tvdbId":9001,"seasonNumber":2,"episodeNumber":4,"episodeFileId":0,"hasFile":false}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var (reader, _) = BuildSonarrReader(configuration, handler);

        var result = await reader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaMatchStatus.Matched, result.Match!.Status);
        Assert.NotNull(result.Metadata);
        Assert.Empty(RequestsFor(handler, "/api/v3/episodeFile"));
        Assert.Equal(1, CountRequests(handler, "/api/v3/series"));
    }

    [Fact]
    public async Task RadarrCachePreservesMatchedMetadataForARecordWithoutAFile()
    {
        var (configuration, radarr, _) = BuildConfiguration();
        var handler = new RecordingHandler(uri =>
            uri.AbsolutePath.EndsWith("/api/v3/movie", StringComparison.Ordinal)
                ? Json("""[{"id":7,"title":"Example","tmdbId":603}]""")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var (reader, _) = BuildRadarrReader(configuration, handler);

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        // The direct read maps an explicit absent-file observation for a matched
        // record without a file; the cached path preserves that same outcome and
        // never issues a movie-file read.
        Assert.True(result.IsSuccess);
        Assert.Equal(MediaMatchStatus.Matched, result.Match!.Status);
        Assert.NotNull(result.Metadata);
        Assert.Equal(
            ArrFilePresence.Absent,
            Assert.IsType<RadarrIdentity>(result.Match.RecordIdentity).MovieFileIdentity.Presence);
        Assert.Empty(RequestsFor(handler, "/api/v3/moviefile"));
        Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));
    }

    [Fact]
    public async Task ConcurrentColdReadersShareOneLibraryRead()
    {
        var (configuration, radarr, _) = BuildConfiguration();
        var handler = new BlockingLibraryHandler(uri => RadarrResponder(uri, fail: false), "/api/v3/movie");
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        var readers = Enumerable.Range(0, 4)
            .Select(_ => reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None))
            .ToArray();

        // Wait for the first (single-flight) library read to be in flight, then
        // give a non-coalesced implementation time to issue its redundant reads.
        await handler.FirstLibraryRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(250);
        handler.Release();

        var results = await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, handler.LibraryRequestCount);
        Assert.Equal(1, inventory.Current.Count);
    }

    [Fact]
    public async Task ProviderFailureReleasesThePopulationGateForALaterAttempt()
    {
        var fail = true;
        var (configuration, radarr, _) = BuildConfiguration(limits => limits.TransientRetryCount = 0);
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        var first = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);
        Assert.False(first.IsSuccess);
        Assert.Equal(0, inventory.Current.Count);

        // The failed population must release the per-connection gate so a later
        // attempt can populate rather than deadlocking or staying poisoned.
        fail = false;
        handler.Clear();
        var second = await reader
            .ReadAsync(MovieIdentity(603), radarr, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(second.IsSuccess);
        Assert.Equal("Bluray-1080p", second.Metadata!.Quality!.Label);
        Assert.Equal(1, inventory.Current.Count);
    }

    [Fact]
    public async Task BulkReadsChunkTheIdListIntoBoundedRequests()
    {
        // A batch size of one forces the repeatable selector into one bounded
        // request per id rather than one unbounded query string.
        var (configuration, radarr, sonarr) = BuildConfiguration(limits => limits.ReconciliationBatchSize = 1);
        var handler = new RecordingHandler(ChunkedBulkResponder);
        var factory = BuildFactory(configuration, handler);

        var radarrResult = await factory.CreateRadarr(radarr)
            .GetMovieFilesAsync(new[] { 7, 8 }, CancellationToken.None);
        var sonarrResult = await factory.CreateSonarr(sonarr)
            .GetEpisodeFilesAsync(new[] { 418, 419 }, CancellationToken.None);

        Assert.True(radarrResult.IsSuccess);
        Assert.Equal(2, radarrResult.Value!.Count);
        Assert.Equal(2, RequestsFor(handler, "/api/v3/moviefile").Count);
        Assert.Contains(handler.Requests, uri => uri.PathAndQuery == "/api/v3/moviefile?movieId=7");
        Assert.Contains(handler.Requests, uri => uri.PathAndQuery == "/api/v3/moviefile?movieId=8");

        Assert.True(sonarrResult.IsSuccess);
        Assert.Equal(2, sonarrResult.Value!.Count);
        Assert.Equal(2, RequestsFor(handler, "/api/v3/episodeFile").Count);
        Assert.Contains(handler.Requests, uri => uri.PathAndQuery == "/api/v3/episodeFile?episodeFileIds=418");
        Assert.Contains(handler.Requests, uri => uri.PathAndQuery == "/api/v3/episodeFile?episodeFileIds=419");
    }

    [Fact]
    public async Task CachedObservationsAreCanonicalAndSecretFree()
    {
        var (configuration, radarr, _) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.True(inventory.Current.TryGet(radarr.ConnectionId, DateTimeOffset.UtcNow, out var entry));
        Assert.NotNull(entry);
        Assert.NotEmpty(entry!.Records);
        Assert.All(entry.Records, record => Assert.IsType<MatchCandidate>(record.Candidate));

        // The only free-text carrier is the bounded, null failure summary; the
        // observation set never carries the API key or a provider DTO.
        Assert.Null(entry.LastError);
        var strings = entry.Records
            .SelectMany(record => ObservationStrings(record))
            .ToArray();
        Assert.NotEmpty(strings);
        Assert.DoesNotContain(strings, value => value.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplacedConfigurationSnapshotRebuildsTheInventoryCacheWithTheNewBounds()
    {
        var (configuration, _, _) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);
        var radarr = ArrConnectionCatalog.FromSnapshot(configuration.Current)
            .Single(connection => connection.Provider.Kind == ArrProviderKind.Radarr);

        await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);
        Assert.Equal(1, inventory.Current.Count);

        var replacement = new PluginConfiguration();
        replacement.Radarr.Enabled = true;
        replacement.Radarr.BaseUrl = "https://radarr.local:7878";
        replacement.Radarr.ApiKey = ApiKey;
        replacement.Sonarr.Enabled = true;
        replacement.Sonarr.BaseUrl = "https://sonarr.local:8989";
        replacement.Sonarr.ApiKey = ApiKey;
        replacement.Limits.InventoryCacheMaxRecords = 2;
        Assert.True(configuration.TryReplace(replacement, out var validation));
        Assert.True(validation.IsValid);

        // A replaced snapshot takes effect immediately: the non-authoritative
        // cache is rebuilt with the new validated bounds rather than silently
        // retaining the previous snapshot's bounds.
        Assert.Equal(0, inventory.Current.Count);
        Assert.Equal(2, inventory.Current.MaxRecordsPerConnection);
    }

    [Fact]
    public async Task BulkReadsWithNoIdentifiersMakeNoProviderRequest()
    {
        var (configuration, radarr, sonarr) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var factory = BuildFactory(configuration, handler);

        var radarrResult = await factory.CreateRadarr(radarr)
            .GetMovieFilesAsync(Array.Empty<int>(), CancellationToken.None);
        var sonarrResult = await factory.CreateSonarr(sonarr)
            .GetEpisodeFilesAsync(Array.Empty<int>(), CancellationToken.None);

        Assert.True(radarrResult.IsSuccess);
        Assert.Empty(radarrResult.Value!);
        Assert.True(sonarrResult.IsSuccess);
        Assert.Empty(sonarrResult.Value!);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BulkReadsRejectNonPositiveIdentifiersWithoutAProviderRequest()
    {
        var (configuration, radarr, sonarr) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var factory = BuildFactory(configuration, handler);

        var radarrResult = await factory.CreateRadarr(radarr)
            .GetMovieFilesAsync(new[] { 7, 0 }, CancellationToken.None);
        var sonarrResult = await factory.CreateSonarr(sonarr)
            .GetEpisodeFilesAsync(new[] { 418, -1 }, CancellationToken.None);

        Assert.False(radarrResult.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, radarrResult.Error!.Code);
        Assert.False(sonarrResult.IsSuccess);
        Assert.Equal(ArrProviderErrorCode.InvalidResponse, sonarrResult.Error!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task InvalidatedObservationSetIsRePopulatedOnTheNextRead()
    {
        var (configuration, radarr, _) = BuildConfiguration();
        var handler = new RecordingHandler(uri => RadarrResponder(uri, fail: false));
        var (reader, inventory) = BuildRadarrReader(configuration, handler);

        await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);
        Assert.Equal(1, inventory.Current.Count);
        Assert.Equal(1, CountRequests(handler, "/api/v3/movie"));

        // An ArrTags-side invalidation source (webhook, library refresh/post-scan,
        // or scheduled/manual reconciliation) discards the retained set; the next
        // read must re-populate from the provider instead of serving the prior set.
        Assert.True(inventory.Invalidate(radarr.ConnectionId));
        Assert.Equal(0, inventory.Current.Count);

        var result = await reader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bluray-1080p", result.Metadata!.Quality!.Label);
        Assert.Equal(2, CountRequests(handler, "/api/v3/movie"));
        Assert.Equal(1, inventory.Current.Count);
    }

    [Fact]
    public async Task ProviderReadsUseNoConditionalRequestRevisionTokenOrHistorySinceWatermark()
    {
        var (configuration, radarr, sonarr) = BuildConfiguration();
        var handler = new CapturingHandler(CombinedResponder);
        var radarrReader = new RadarrMetadataReader(
            BuildFactory(configuration, handler), log: null, inventory: new ArrInventoryCacheProvider(configuration));
        var sonarrReader = new SonarrMetadataReader(
            BuildFactory(configuration, handler), log: null, inventory: new ArrInventoryCacheProvider(configuration));

        await radarrReader.ReadAsync(MovieIdentity(603), radarr, CancellationToken.None);
        await sonarrReader.ReadAsync(EpisodeIdentity(12345, 9001), sonarr, CancellationToken.None);

        Assert.NotEmpty(handler.Requests);
        foreach (var request in handler.Requests)
        {
            // No provider conditional request: the reads never send a validator.
            Assert.DoesNotContain("If-None-Match", request.HeaderNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("If-Modified-Since", request.HeaderNames, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("If-Match", request.HeaderNames, StringComparer.OrdinalIgnoreCase);

            // The request set is pinned: every request must target a documented
            // reconciliation endpoint, so an unnamed revision-token or
            // history/since fetch (or any other unlisted path) fails the test
            // rather than passing because it avoided the literal markers.
            Assert.Contains(request.Uri.AbsolutePath, AllowedProviderEndpointPaths);

            // The query is pinned too: no history watermark or cacheable-bypass
            // parameter, and no parameter outside the documented selectors.
            Assert.DoesNotContain("history/since", request.Uri.PathAndQuery, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("h=", request.Uri.PathAndQuery, StringComparison.OrdinalIgnoreCase);
            foreach (var key in QueryKeys(request.Uri))
            {
                Assert.Contains(key, AllowedProviderQueryKeys);
            }
        }
    }

    private static IEnumerable<string> QueryKeys(Uri uri)
    {
        var query = uri.Query;
        if (query.StartsWith('?'))
        {
            query = query[1..];
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                yield return pair[..separator];
            }
        }
    }

    private static HttpResponseMessage CombinedResponder(Uri uri)
    {
        var response = RadarrResponder(uri, fail: false);
        return response.StatusCode == HttpStatusCode.NotFound
            ? SonarrResponder(uri, embeddedFile: true)
            : response;
    }

    private static IEnumerable<string> ObservationStrings(ArrInventoryRecordObservation observation)
    {
        var candidate = observation.Candidate;
        if (candidate.Title is { } title)
        {
            yield return title;
        }

        if (candidate.PrimaryPath is { } path)
        {
            yield return path;
        }

        foreach (var pair in candidate.ProviderIds)
        {
            yield return pair.Key;
            yield return pair.Value;
        }

        if (observation.FileObservation is { } metadata)
        {
            foreach (var value in new[]
            {
                metadata.Quality?.Label,
                metadata.Quality?.Source,
                metadata.Quality?.Modifier,
                metadata.VideoCodec,
                metadata.AudioCodec,
                metadata.Source,
                metadata.Resolution?.Label,
                metadata.DynamicRange?.Profile,
            })
            {
                if (value is not null)
                {
                    yield return value;
                }
            }

            foreach (var badge in metadata.CustomBadges)
            {
                yield return badge;
            }
        }
    }

    /// <summary>
    /// Stores one canonical Radarr observation directly into the cache at a
    /// chosen observation time, so a stale-but-usable or expired reader boundary
    /// can be exercised without an injectable clock.
    /// </summary>
    private static void StoreRadarrObservation(
        ArrInventoryCache cache,
        ArrConnection connection,
        DateTimeOffset observedAt,
        int movieId,
        int tmdbId,
        int fileId)
    {
        var movie = new RadarrMovieResource { Id = movieId, Title = "Example", TmdbId = tmdbId, MovieFileId = fileId };
        var candidate = RadarrMatchCandidateFactory.FromMovie(connection, movie);
        var file = new RadarrMovieFileResource
        {
            Id = fileId,
            MovieId = movieId,
            Quality = new RadarrQualityModel
            {
                Quality = new RadarrQuality { Id = 7, Name = "Bluray-1080p", Source = "bluray", Resolution = 1080 },
            },
        };
        var metadata = RadarrMetadataMapper.Map(connection, movie, file, observedAt);
        var observation = new ArrInventoryRecordObservation(candidate, metadata);

        Assert.True(cache.TryStore(connection, observedAt, new[] { observation }));
    }

    private static (RadarrMetadataReader Reader, ArrInventoryCacheProvider Inventory) BuildRadarrReader(
        ConfigurationSnapshotService configuration,
        HttpMessageHandler handler)
    {
        var factory = BuildFactory(configuration, handler);
        var inventory = new ArrInventoryCacheProvider(configuration);
        var reader = new RadarrMetadataReader(factory, log: null, inventory: inventory);
        return (reader, inventory);
    }

    private static (SonarrMetadataReader Reader, ArrInventoryCacheProvider Inventory) BuildSonarrReader(
        ConfigurationSnapshotService configuration,
        HttpMessageHandler handler)
    {
        var factory = BuildFactory(configuration, handler);
        var inventory = new ArrInventoryCacheProvider(configuration);
        var reader = new SonarrMetadataReader(factory, log: null, inventory: inventory);
        return (reader, inventory);
    }

    private static ArrReadClientFactory BuildFactory(
        ConfigurationSnapshotService configuration,
        HttpMessageHandler handler)
    {
        var httpFactory = new ArrHttpClientFactory(new StubHttpClientFactory(handler));
        return new ArrReadClientFactory(httpFactory, configuration, configuration);
    }

    private static (ConfigurationSnapshotService Configuration, ArrConnection Radarr, ArrConnection Sonarr) BuildConfiguration(
        Action<OperationalLimits>? configureLimits = null)
    {
        var configuration = new PluginConfiguration();
        configuration.Radarr.Enabled = true;
        configuration.Radarr.BaseUrl = "https://radarr.local:7878";
        configuration.Radarr.ApiKey = ApiKey;
        configuration.Sonarr.Enabled = true;
        configuration.Sonarr.BaseUrl = "https://sonarr.local:8989";
        configuration.Sonarr.ApiKey = ApiKey;
        configureLimits?.Invoke(configuration.Limits);

        var service = new ConfigurationSnapshotService(configuration);
        var connections = ArrConnectionCatalog.FromSnapshot(service.Current);
        return (
            service,
            connections.Single(connection => connection.Provider.Kind == ArrProviderKind.Radarr),
            connections.Single(connection => connection.Provider.Kind == ArrProviderKind.Sonarr));
    }

    private static MediaIdentity MovieIdentity(int tmdbId)
    {
        return new MediaIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            MediaItemType.Movie,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tmdb"] = tmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
    }

    private static MediaIdentity EpisodeIdentity(int seriesTvdbId, int episodeTvdbId)
    {
        var series = new MediaIdentity(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            MediaItemType.Series,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tvdb"] = seriesTvdbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        return new MediaIdentity(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            MediaItemType.Episode,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tvdb"] = episodeTvdbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            seriesIdentity: series,
            seasonNumber: 2,
            episodeNumber: 4);
    }

    private static HttpResponseMessage RadarrResponder(Uri uri, bool fail)
    {
        if (fail)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (uri.AbsolutePath.EndsWith("/api/v3/movie", StringComparison.Ordinal))
        {
            return Json("""
                [
                  {"id":7,"title":"Example","tmdbId":603,"movieFileId":42},
                  {"id":8,"title":"Other","tmdbId":604,"movieFileId":43}
                ]
                """);
        }

        if (uri.AbsolutePath.EndsWith("/api/v3/moviefile", StringComparison.Ordinal))
        {
            return Json("""
                [
                  {"id":42,"movieId":7,"quality":{"quality":{"id":7,"name":"Bluray-1080p","source":"bluray","resolution":1080}},"mediaInfo":{"videoCodec":"h264","width":1920,"height":1080}},
                  {"id":43,"movieId":8,"quality":{"quality":{"id":7,"name":"Bluray-1080p","source":"bluray","resolution":1080}},"mediaInfo":{"videoCodec":"h264","width":1920,"height":1080}}
                ]
                """);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Serves the Radarr library and the matched movie's file per record, but
    /// fails the population's bulk (multi-id) movie-file request so the direct
    /// fallback can be observed.
    /// </summary>
    private static HttpResponseMessage RadarrBulkFileFailureResponder(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith("/api/v3/movie", StringComparison.Ordinal))
        {
            return Json("""
                [
                  {"id":7,"title":"Example","tmdbId":603,"movieFileId":42},
                  {"id":8,"title":"Other","tmdbId":604,"movieFileId":43}
                ]
                """);
        }

        if (uri.AbsolutePath.EndsWith("/api/v3/moviefile", StringComparison.Ordinal))
        {
            if (QueryValues(uri, "movieId").Count != 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return Json("""
                [
                  {"id":42,"movieId":7,"quality":{"quality":{"id":7,"name":"Bluray-1080p","source":"bluray","resolution":1080}},"mediaInfo":{"videoCodec":"h264","width":1920,"height":1080}}
                ]
                """);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Serves a two-series Sonarr library where the non-matched series' episode
    /// read fails, so the population stores nothing and the direct fallback for
    /// the matched series can be observed.
    /// </summary>
    private static HttpResponseMessage SonarrEpisodeReadFailureResponder(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith("/api/v3/series", StringComparison.Ordinal))
        {
            return Json("""[{"id":12,"title":"Example","tvdbId":12345},{"id":13,"title":"Other","tvdbId":54321}]""");
        }

        if (uri.AbsolutePath.EndsWith("/api/v3/episode", StringComparison.Ordinal))
        {
            if (GetQueryValue(uri, "seriesId") == "13")
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return Json($$"""
                [
                  {"id":73,"seriesId":12,"tvdbId":9001,"seasonNumber":2,"episodeNumber":4,"episodeFileId":418,"hasFile":true{{EmbeddedEpisodeFile(418)}}}
                ]
                """);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage SonarrResponder(Uri uri, bool embeddedFile)
    {
        if (uri.AbsolutePath.EndsWith("/api/v3/series", StringComparison.Ordinal))
        {
            return Json("""[{"id":12,"title":"Example","tvdbId":12345}]""");
        }

        if (uri.AbsolutePath.EndsWith("/api/v3/episode", StringComparison.Ordinal))
        {
            var embedded73 = embeddedFile ? EmbeddedEpisodeFile(418) : string.Empty;
            var embedded74 = embeddedFile ? EmbeddedEpisodeFile(419) : string.Empty;
            return Json($$"""
                [
                  {"id":73,"seriesId":12,"tvdbId":9001,"seasonNumber":2,"episodeNumber":4,"episodeFileId":418,"hasFile":true{{embedded73}}},
                  {"id":74,"seriesId":12,"tvdbId":9002,"seasonNumber":2,"episodeNumber":5,"episodeFileId":419,"hasFile":true{{embedded74}}}
                ]
                """);
        }

        if (uri.AbsolutePath.EndsWith("/api/v3/episodeFile", StringComparison.Ordinal))
        {
            return Json("""
                [
                  {"id":418,"seriesId":12,"quality":{"quality":{"id":3,"name":"WEBDL-1080p","source":"webdl","resolution":1080}},"mediaInfo":{"audioCodec":"EAC3","videoCodec":"h264"}},
                  {"id":419,"seriesId":12,"quality":{"quality":{"id":3,"name":"WEBDL-1080p","source":"webdl","resolution":1080}},"mediaInfo":{"audioCodec":"EAC3","videoCodec":"h264"}}
                ]
                """);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string EmbeddedEpisodeFile(int fileId)
    {
        return $$$""","episodeFile":{"id":{{{fileId}}},"seriesId":12,"quality":{"quality":{"id":3,"name":"WEBDL-1080p","source":"webdl","resolution":1080}},"mediaInfo":{"audioCodec":"EAC3","videoCodec":"h264"}}""";
    }

    private static HttpResponseMessage ChunkedBulkResponder(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith("/api/v3/moviefile", StringComparison.Ordinal))
        {
            var movieId = int.Parse(GetQueryValue(uri, "movieId")!, CultureInfo.InvariantCulture);
            return Json($$"""[{"id":{{movieId}},"movieId":{{movieId}}}]""");
        }

        if (uri.AbsolutePath.EndsWith("/api/v3/episodeFile", StringComparison.Ordinal))
        {
            var fileId = int.Parse(GetQueryValue(uri, "episodeFileIds")!, CultureInfo.InvariantCulture);
            return Json($$"""[{"id":{{fileId}},"seriesId":12}]""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        var query = uri.Query;
        if (query.StartsWith('?'))
        {
            query = query[1..];
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && pair[..separator] == key)
            {
                return pair[(separator + 1)..];
            }
        }

        return null;
    }

    private static IReadOnlyList<string> QueryValues(Uri uri, string key)
    {
        var query = uri.Query;
        if (query.StartsWith('?'))
        {
            query = query[1..];
        }

        var values = new List<string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && pair[..separator] == key)
            {
                values.Add(pair[(separator + 1)..]);
            }
        }

        return values;
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static int CountRequests(RecordingHandler handler, string path)
    {
        return handler.Requests.Count(uri => uri.AbsolutePath == path);
    }

    private static IReadOnlyList<Uri> RequestsFor(RecordingHandler handler, string path)
    {
        return handler.Requests.Where(uri => uri.AbsolutePath == path).ToArray();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpResponseMessage> _responder;

        public RecordingHandler(Func<Uri, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<Uri> Requests { get; } = new();

        public void Clear()
        {
            Requests.Clear();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responder(request.RequestUri!));
        }
    }

    /// <summary>
    /// Records the request URI and header names so a test can assert the provider
    /// reads send no conditional validator or revision-token poll. It snapshots
    /// the values at send time rather than retaining the (possibly disposed)
    /// request message.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpResponseMessage> _responder;

        public CapturingHandler(Func<Uri, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headerNames = request.Headers.Select(header => header.Key).ToArray();
            Requests.Add(new CapturedRequest(request.RequestUri!, headerNames));
            return Task.FromResult(_responder(request.RequestUri!));
        }
    }

    private sealed record CapturedRequest(Uri Uri, IReadOnlyList<string> HeaderNames);

    /// <summary>
    /// Records requests and holds the first library request open so a concurrent
    /// cold-start population can be observed deterministically.
    /// </summary>
    private sealed class BlockingLibraryHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpResponseMessage> _responder;
        private readonly string _blockPath;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _libraryRequests;

        public BlockingLibraryHandler(Func<Uri, HttpResponseMessage> responder, string blockPath)
        {
            _responder = responder;
            _blockPath = blockPath;
        }

        public List<Uri> Requests { get; } = new();

        public TaskCompletionSource FirstLibraryRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LibraryRequestCount => Volatile.Read(ref _libraryRequests);

        public void Release()
        {
            _release.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == _blockPath
                && Interlocked.Increment(ref _libraryRequests) == 1)
            {
                FirstLibraryRequest.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }

            return _responder(request.RequestUri!);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}
