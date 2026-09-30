using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Concurrency;
using ArrTags.Configuration;
using ArrTags.Diagnostics;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.State;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 17.1 focused checks that the bounded diagnostics counters are recorded
/// at each existing bounded boundary: the provider inventory cache hit/miss
/// decision, the matching/reconciliation classification, the stale-metadata
/// transition, the render-failure classification, and the per-connection
/// provider health. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class DiagnosticsInstrumentationTests : IDisposable
{
    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly MetadataStateStore _store;
    private readonly DiagnosticsMetrics _metrics = new();

    public DiagnosticsInstrumentationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new MetadataStateStore(_repository);
    }

    // ---- Cache hits and misses ----------------------------------------------

    [Fact]
    public async Task CacheHitAndMissAreCountedAtTheInventoryBoundary()
    {
        var configuration = new PluginConfiguration
        {
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-key",
            },
        };
        var snapshots = new ConfigurationSnapshotService(configuration);
        var connection = ArrConnectionCatalog
            .FromSnapshot(snapshots.Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Radarr);
        var inventory = new ArrInventoryCacheProvider(snapshots);
        var client = new FakeRadarrReadClient(connection)
        {
            Movies = ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(new[]
            {
                new RadarrMovieResource { Id = 7, Title = "Example", TmdbId = 603, MovieFileId = 42 },
            }),
            Files = ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(new[]
            {
                new RadarrMovieFileResource
                {
                    Id = 42,
                    MovieId = 7,
                    Quality = new RadarrQualityModel
                    {
                        Quality = new RadarrQuality { Id = 7, Name = "Bluray-1080p", Source = "bluray", Resolution = 1080 },
                    },
                },
            }),
        };
        var reader = new RadarrMetadataReader(
            new FakeReadClientFactory { Radarr = client },
            inventory: inventory,
            metrics: _metrics);
        var identity = MovieIdentity(603);

        // The cold read populates the cache through the provider; the second
        // read is served by the retained observation set.
        var cold = await reader.ReadAsync(identity, connection, CancellationToken.None);
        var warm = await reader.ReadAsync(identity, connection, CancellationToken.None);

        Assert.True(cold.IsSuccess);
        Assert.True(warm.IsSuccess);

        var snapshot = _metrics.Capture(0, 0);
        Assert.Equal(1, snapshot.CacheMisses);
        Assert.Equal(1, snapshot.CacheHits);
    }

    // ---- Matching failures ---------------------------------------------------

    [Theory]
    [InlineData(MediaMatchStatus.NotFound, 1L, 0L, 0L)]
    [InlineData(MediaMatchStatus.Ambiguous, 0L, 1L, 0L)]
    [InlineData(MediaMatchStatus.Unsupported, 0L, 0L, 1L)]
    [InlineData(MediaMatchStatus.Matched, 0L, 0L, 0L)]
    [InlineData(MediaMatchStatus.Stale, 0L, 0L, 0L)]
    public async Task MatchingFailuresAreCountedByClassification(
        MediaMatchStatus status,
        long expectedNotFound,
        long expectedAmbiguous,
        long expectedUnsupported)
    {
        var configuration = ReconciliationFixtures.Configuration();
        var resolver = SeededResolver();
        var reader = new FakeMetadataReader { Kind = ArrProviderKind.Radarr };
        reader.Handler = (identity, connection, _) => MatchingOutcome(identity, connection, status);
        var processor = new MetadataReconciliationProcessor(
            configuration,
            resolver,
            new IArrMetadataReader[] { reader },
            _store,
            metrics: _metrics);

        var result = await processor.ProcessAsync(
            WorkItem(configuration.Current.ConfigurationVersion),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var snapshot = _metrics.Capture(0, 0);
        Assert.Equal(expectedNotFound, snapshot.MatchingFailures.NotFound);
        Assert.Equal(expectedAmbiguous, snapshot.MatchingFailures.Ambiguous);
        Assert.Equal(expectedUnsupported, snapshot.MatchingFailures.Unsupported);
    }

    // ---- Stale metadata ------------------------------------------------------

    [Fact]
    public async Task StaleMetadataTransitionIsCountedOncePerFreshRecord()
    {
        var configuration = ReconciliationFixtures.Configuration();
        var resolver = SeededResolver();
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        _store.Write(MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            DateTimeOffset.UtcNow,
            staleWindow: TimeSpan.FromMinutes(configuration.Current.Limits.MetadataStaleWindowMinutes)));

        var reader = new FakeMetadataReader { Kind = ArrProviderKind.Radarr };
        reader.Result = ArrMetadataReadResult.Failure(new ArrProviderError(
            ArrProviderErrorCode.ProviderUnavailable,
            ArrErrorRetryability.Later,
            "The provider was unavailable."));
        var processor = new MetadataReconciliationProcessor(
            configuration,
            resolver,
            new IArrMetadataReader[] { reader },
            _store,
            metrics: _metrics);

        await processor.ProcessAsync(WorkItem(configuration.Current.ConfigurationVersion), CancellationToken.None);

        Assert.Equal(1, _metrics.Capture(0, 0).StaleMetadataTransitions);
        Assert.Equal(
            MetadataStateKind.Stale,
            _store.Read(ReconciliationFixtures.ItemId, ArrProviderKind.Radarr).Value!.State);

        // The record is already stale, so a second retryable outage is not a new
        // fresh-to-stale transition.
        await processor.ProcessAsync(WorkItem(configuration.Current.ConfigurationVersion), CancellationToken.None);

        Assert.Equal(1, _metrics.Capture(0, 0).StaleMetadataTransitions);
    }

    // ---- Render failures -----------------------------------------------------

    [Theory]
    [InlineData(RenderFailureReason.DecodeFailed)]
    [InlineData(RenderFailureReason.EncodeFailed)]
    [InlineData(RenderFailureReason.Cancelled)]
    public async Task RenderFailureIsCountedByBoundedClassification(RenderFailureReason reason)
    {
        var result = await GenerateAsync(RenderResult.Failed(reason));

        Assert.Equal(ArtworkGenerationOutcome.RenderFailed, result.Outcome);
        Assert.Equal(reason, result.FailureReason);

        var snapshot = _metrics.Capture(0, 0);
        Assert.Equal(1, ReadRenderFailure(snapshot, reason));
        foreach (var other in Enum.GetValues<RenderFailureReason>())
        {
            if (other != reason)
            {
                Assert.Equal(0, ReadRenderFailure(snapshot, other));
            }
        }
    }

    [Fact]
    public async Task RenderPassThroughIsNotCountedAsAFailure()
    {
        var result = await GenerateAsync(RenderResult.PassThrough(RenderPassThroughReason.NoMetadata));

        Assert.Equal(ArtworkGenerationOutcome.RenderPassThrough, result.Outcome);

        var snapshot = _metrics.Capture(0, 0);
        foreach (var reason in Enum.GetValues<RenderFailureReason>())
        {
            Assert.Equal(0, ReadRenderFailure(snapshot, reason));
        }
    }

    // ---- Provider connection health ------------------------------------------

    [Theory]
    [InlineData(null, ArrConnectionHealth.Healthy)]
    [InlineData(ArrProviderErrorCode.AuthenticationFailed, ArrConnectionHealth.AuthenticationFailed)]
    [InlineData(ArrProviderErrorCode.ProviderUnavailable, ArrConnectionHealth.Unavailable)]
    [InlineData(ArrProviderErrorCode.ProviderIncompatible, ArrConnectionHealth.Incompatible)]
    [InlineData(ArrProviderErrorCode.InvalidResponse, ArrConnectionHealth.Unknown)]
    public async Task ProviderHealthIsRecordedPerConnection(
        ArrProviderErrorCode? errorCode,
        ArrConnectionHealth expected)
    {
        var configuration = ReconciliationFixtures.Configuration();
        var connection = ArrConnectionCatalog
            .FromSnapshot(configuration.Current)
            .Single(candidate => candidate.Provider.Kind == ArrProviderKind.Radarr);
        var identity = ReconciliationFixtures.MovieIdentity();
        var reader = new FakeMetadataReader { Kind = ArrProviderKind.Radarr };
        reader.Result = errorCode is { } code
            ? ArrMetadataReadResult.Failure(new ArrProviderError(
                code,
                ArrErrorRetryability.Never,
                "The bounded provider failure."))
            : ArrMetadataReadResult.Success(ReconciliationFixtures.MatchedMovieMatch(identity));
        using var limiter = new ProviderConcurrencyLimiter(configuration);
        var decorator = new ConcurrencyLimitedArrMetadataReader<FakeMetadataReader>(
            reader,
            limiter,
            log: null,
            metrics: _metrics);

        await decorator.ReadAsync(identity, connection, CancellationToken.None);

        var snapshot = _metrics.Capture(0, 0);
        Assert.Equal(expected, snapshot.RadarrHealth);
        Assert.Equal(ArrConnectionHealth.Unknown, snapshot.SonarrHealth);
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

    private async Task<ArtworkGenerationResult> GenerateAsync(RenderResult renderResult)
    {
        var artifacts = new SourceArtifactStore(_repository);
        var states = new PublishedArtworkStateStore(_repository);
        var operations = new ArtworkOperationStore(_repository);
        var host = new PresentSourceHost();
        var publisher = new ArtworkPublisher(host, host, artifacts, states, operations, new OperationalLimits());
        var renderer = new FixedRenderer { Result = renderResult };
        var coordinator = new ArtworkGenerationCoordinator(
            host,
            renderer,
            publisher,
            states,
            artifacts,
            log: null,
            metrics: _metrics);

        return await coordinator.GenerateAsync(Request(), CancellationToken.None);
    }

    private static ArtworkGenerationRequest Request()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        return new ArtworkGenerationRequest(
            RenderTestFixtures.ItemId,
            ArtworkImageSurface.Primary,
            identity,
            RenderTestFixtures.BuildMatch(identity),
            RenderTestFixtures.BuildMetadata(),
            BadgeDefinition.V1Default,
            "CONFIG-TEST");
    }

    private static long ReadRenderFailure(DiagnosticsSnapshot snapshot, RenderFailureReason reason)
    {
        var property = typeof(RenderFailureCounts).GetProperty(reason.ToString());
        Assert.NotNull(property);
        return (long)property!.GetValue(snapshot.RenderFailures)!;
    }

    private static ArrMetadataReadResult MatchingOutcome(
        MediaIdentity identity,
        ArrConnection connection,
        MediaMatchStatus status)
    {
        if (status == MediaMatchStatus.Matched)
        {
            var record = new RadarrIdentity(connection.ConnectionId, 42, ArrFileIdentity.Present(84));
            var match = new MediaMatch(
                identity,
                connection.Provider,
                connection.ConnectionId,
                MediaMatchStatus.Matched,
                MediaMatchMethod.ProviderId,
                record,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["tmdb"] = "603" });
            return ArrMetadataReadResult.Success(match, ReconciliationFixtures.MovieMetadata(identity, record));
        }

        return ArrMetadataReadResult.Success(new MediaMatch(
            identity,
            connection.Provider,
            connection.ConnectionId,
            status,
            MediaMatchMethod.None,
            ambiguityReason: "The bounded matching classification."));
    }

    private static ReconciliationLibraryResolver SeededResolver()
    {
        var resolver = new ReconciliationLibraryResolver();
        resolver.LibraryIds[ReconciliationFixtures.ItemId] = ReconciliationFixtures.LibraryId;
        resolver.Items[ReconciliationFixtures.ItemId] =
            ReconciliationFixtures.Movie(ReconciliationFixtures.ItemId, ReconciliationFixtures.LibraryId);
        return resolver;
    }

    private static LibraryWorkItem WorkItem(long configurationVersion)
    {
        return new LibraryWorkItem(
            new WorkItemKey(ReconciliationFixtures.ItemId, null, ArtworkImageSurface.Primary),
            LibraryWorkReason.Updated,
            configurationVersion);
    }

    private static MediaIdentity MovieIdentity(int tmdbId)
    {
        return new MediaIdentity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            MediaItemType.Movie,
            providerIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tmdb"] = tmdbId.ToString(CultureInfo.InvariantCulture),
            });
    }

    private sealed class FakeReadClientFactory : IArrReadClientFactory
    {
        public IRadarrReadClient? Radarr { get; set; }

        public IRadarrReadClient CreateRadarr(ArrConnection connection)
        {
            return Radarr ?? throw new InvalidOperationException("No fake Radarr client was configured.");
        }

        public ISonarrReadClient CreateSonarr(ArrConnection connection)
        {
            throw new InvalidOperationException("No fake Sonarr client was configured.");
        }
    }

    private sealed class FakeRadarrReadClient : IRadarrReadClient
    {
        public FakeRadarrReadClient(ArrConnection connection)
        {
            Connection = connection;
        }

        public ArrProviderKind Kind => ArrProviderKind.Radarr;

        public ArrConnection Connection { get; }

        public ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>> Movies { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<RadarrMovieResource>>(Array.Empty<RadarrMovieResource>());

        public ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>> Files { get; set; } =
            ArrProviderResults.Success<IReadOnlyList<RadarrMovieFileResource>>(Array.Empty<RadarrMovieFileResource>());

        public Task<ArrConnectionProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieResource>>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Movies);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(int movieId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Files);
        }

        public Task<ArrProviderReadResult<IReadOnlyList<RadarrMovieFileResource>>> GetMovieFilesAsync(
            IReadOnlyList<int> movieIds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Files);
        }
    }

    private sealed class FixedRenderer : IRenderer
    {
        public RenderResult Result { get; set; } = RenderResult.Failed(RenderFailureReason.RenderError);

        public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    private sealed class PresentSourceHost : IArtworkSourceReader, IArtworkImageWriter
    {
        private static readonly byte[] SourceBytes =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3,
        };

        public Task<ArtworkSourceReadResult> ReadAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ArtworkSourceReadResult.Present(
                surface,
                "image/png",
                SourceBytes,
                ArtworkHashes.ComputeSha256(SourceBytes),
                100,
                150,
                DateTimeOffset.UnixEpoch,
                "tag-1"));
        }

        public Task<ArtworkImageMutationResult> SaveImageAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            ReadOnlyMemory<byte> content,
            string contentType,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("A failed render must never reach the image writer.");
        }

        public Task<ArtworkImageMutationResult> PersistItemUpdateAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("A failed render must never reach the image writer.");
        }

        public Task<ArtworkImageMutationResult> RemoveImageAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("A failed render must never reach the image writer.");
        }
    }
}
