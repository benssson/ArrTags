using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Rendering;
using ArrTags.State;
using ArrTags.Updates;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 16 task 16.2 behavioral evidence for the restart-required
/// classification (ADR-028 clause 2). Each probe drives a real consumer: a
/// restart-required setting must be captured when the singleton is constructed
/// (a later replacement snapshot must not change the captured consumer), and a
/// per-operation setting must observe the current snapshot without rebuilding
/// the consumer. These probes fail if a classified consumer changes its
/// resolution site, which is the sensitivity the classification depends on.
/// </summary>
public sealed class RestartRequiredConsumerEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "arrtags-restart-required-" + Guid.NewGuid().ToString("N"));

    public RestartRequiredConsumerEvidenceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void StateRepositoryUsesTheSnapshotLimitsCapturedAtConstruction()
    {
        // Mirrors ArrTagsServiceRegistrator.CreateStateRepository: the singleton
        // is constructed with the limits of the snapshot active at that moment.
        var configuration = new ConfigurationSnapshotService(new PluginConfiguration
        {
            Limits = new OperationalLimits
            {
                RenderCacheTtlMinutes = 1,
                TerminalProvenanceRetentionDays = 1,
            },
        });
        var repository = new StateRepository(_root, configuration.Current.Limits);

        repository.Write(StateAuthority.Cache, "metadata", "cached", new TestPayload { Name = "cached" });
        File.SetLastWriteTimeUtc(
            repository.Paths.GetRecordPath(StateAuthority.Cache, "metadata", "cached"),
            DateTime.UtcNow.AddHours(-2));
        WriteAuthoritative(repository, "terminal", DateTimeOffset.UtcNow.AddDays(-5), terminal: true);
        WriteAuthoritative(repository, "active", DateTimeOffset.UtcNow.AddDays(-5), terminal: false);

        // A replacement snapshot with far larger windows must not change the
        // construction-captured repository limits (per operation it would).
        Assert.True(configuration.TryReplace(
            new PluginConfiguration
            {
                Limits = new OperationalLimits
                {
                    RenderCacheTtlMinutes = 43200,
                    TerminalProvenanceRetentionDays = 365,
                },
            },
            out _));

        var removed = repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.Equal(2, removed);
        Assert.Equal(
            StateReadStatus.Missing,
            repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "cached").Status);
        Assert.Equal(
            StateReadStatus.Missing,
            repository.Read<TestPayload>(StateAuthority.Authoritative, "artwork", "terminal").Status);
        Assert.Equal(
            StateReadStatus.Found,
            repository.Read<TestPayload>(StateAuthority.Authoritative, "artwork", "active").Status);
    }

    [Fact]
    public void StateRepositoryLimitsDoNotTrackAReplacementSnapshot()
    {
        // Mirrors ArrTagsServiceRegistrator.CreateStateRepository: the singleton
        // is constructed with the limits of the snapshot active at that moment.
        var configuration = new ConfigurationSnapshotService(new PluginConfiguration());
        var capturedTtl = configuration.Current.Limits.RenderCacheTtlMinutes;
        var capturedQuota = configuration.Current.Limits.RenderCacheQuotaBytes;
        var capturedRetention = configuration.Current.Limits.TerminalProvenanceRetentionDays;
        var repository = new StateRepository(_root, configuration.Current.Limits);

        Assert.True(configuration.TryReplace(
            new PluginConfiguration
            {
                Limits = new OperationalLimits
                {
                    RenderCacheTtlMinutes = 1,
                    RenderCacheQuotaBytes = 64L * 1024 * 1024,
                    TerminalProvenanceRetentionDays = 365,
                },
            },
            out _));

        // The replacement is a distinct validated snapshot; the repository's
        // captured limits keep the construction-time values.
        Assert.Equal(capturedTtl, repository.Limits.RenderCacheTtlMinutes);
        Assert.Equal(capturedQuota, repository.Limits.RenderCacheQuotaBytes);
        Assert.Equal(capturedRetention, repository.Limits.TerminalProvenanceRetentionDays);
        Assert.NotEqual(capturedTtl, configuration.Current.Limits.RenderCacheTtlMinutes);
        Assert.NotEqual(capturedQuota, configuration.Current.Limits.RenderCacheQuotaBytes);
        Assert.NotEqual(capturedRetention, configuration.Current.Limits.TerminalProvenanceRetentionDays);
    }

    [Fact]
    public void SourceArtifactStoreUsesTheSourceLimitAndQuotaCapturedAtConstruction()
    {
        var limits = new OperationalLimits
        {
            SourceArtifactLimitBytes = 4,
            ArtifactStorageQuotaBytes = 4,
        };
        var repository = new StateRepository(_root, limits);
        var store = new SourceArtifactStore(repository);

        // A replacement snapshot is a different limits object; even mutating
        // this one must not change the values the store copied at construction.
        limits.SourceArtifactLimitBytes = 1024 * 1024;
        limits.ArtifactStorageQuotaBytes = 1024 * 1024;

        var oversized = store.Promote(new byte[8], "image/png");
        Assert.False(oversized.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.TooLarge, oversized.Failure);

        Assert.True(store.Promote(new byte[] { 7, 7, 7, 7 }, "image/png").Succeeded);
        var overQuota = store.Promote(new byte[] { 1, 2, 3, 4 }, "image/png");
        Assert.False(overQuota.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.StorageQuotaExceeded, overQuota.Failure);
    }

    [Fact]
    public async Task ArtworkSourceReaderUsesTheByteAndDimensionLimitsCapturedAtConstruction()
    {
        var byteLimits = new OperationalLimits { SourceArtifactLimitBytes = 4 };
        var byteReader = new ArtworkSourceReader(new PresentAccess(PngBytes(8), 10, 10), byteLimits);
        byteLimits.SourceArtifactLimitBytes = 1024 * 1024;

        var byteResult = await byteReader.ReadAsync(Guid.NewGuid(), ArtworkImageSurface.Primary, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, byteResult.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.SourceTooLarge, byteResult.FailureReason);

        var dimensionLimits = new OperationalLimits { MaxImageDimensionPixels = 1024 };
        var dimensionReader = new ArtworkSourceReader(new PresentAccess(PngBytes(16), 1025, 100), dimensionLimits);
        dimensionLimits.MaxImageDimensionPixels = 16384;

        var dimensionResult = await dimensionReader.ReadAsync(Guid.NewGuid(), ArtworkImageSurface.Primary, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, dimensionResult.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.DimensionTooLarge, dimensionResult.FailureReason);
    }

    [Fact]
    public async Task ArtworkPublisherUsesTheDerivedLimitCapturedAtConstruction()
    {
        var repository = new StateRepository(_root);
        var limits = new OperationalLimits { DerivedArtifactLimitBytes = 64L * 1024 };
        var publisher = new ArtworkPublisher(
            new PresentReader(),
            new NeverCalledWriter(),
            new SourceArtifactStore(repository),
            new PublishedArtworkStateStore(repository),
            new ArtworkOperationStore(repository),
            limits);

        // A replacement snapshot is a different limits object; even mutating
        // this one must not change the limit the publisher copied at construction.
        limits.DerivedArtifactLimitBytes = long.MaxValue;

        var derived = new byte[(64 * 1024) + 1];
        PngBytes(11).CopyTo(derived, 0);
        var request = new ArtworkPublicationRequest(
            Guid.NewGuid(),
            ArtworkImageSurface.Primary,
            RenderResult.Rendered(
                derived,
                100,
                150,
                ArtworkHashes.ComputeSha256(derived),
                ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("restart-required-probe"))));

        var result = await publisher.PublishAsync(request, CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.DerivedArtifactRejected, result.Outcome);
    }

    [Fact]
    public void LibraryWorkQueueCapacityIsResolvedFromTheCurrentLimitsProvider()
    {
        var current = new OperationalLimits { QueueCapacity = 3 };
        var queue = new LibraryWorkQueue(() => current);

        Assert.Equal(3, queue.Capacity);

        // A replaced snapshot is a different limits object resolved per operation.
        current = new OperationalLimits { QueueCapacity = 7 };

        Assert.Equal(7, queue.Capacity);
    }

    [Fact]
    public void LogVerbosityGateIsResolvedFromTheCurrentSnapshotPerCall()
    {
        var configuration = new ConfigurationSnapshotService(new PluginConfiguration
        {
            LogVerbosity = LogVerbosity.Warning,
        });
        var gate = new LogVerbosityGate(configuration);

        Assert.Equal(LogLevel.Warning, gate.EffectiveLevel);

        Assert.True(configuration.TryReplace(
            new PluginConfiguration { LogVerbosity = LogVerbosity.Debug },
            out _));

        Assert.Equal(LogLevel.Debug, gate.EffectiveLevel);
    }

    /// <inheritdoc />
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

    private static void WriteAuthoritative(StateRepository repository, string recordId, DateTimeOffset updatedAt, bool terminal)
    {
        AtomicFileWriter.Write(
            repository.Paths.GetRecordPath(StateAuthority.Authoritative, "artwork", recordId),
            StateEnvelopeCodec.Serialize(
                StateAuthority.Authoritative,
                "artwork",
                recordId,
                new TestPayload { Name = recordId },
                updatedAt,
                terminal));
    }

    private static byte[] PngBytes(int length)
    {
        var bytes = new byte[Math.Max(length, 8)];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        signature.CopyTo(bytes, 0);
        return bytes;
    }

    private sealed class TestPayload
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PresentAccess : IArtworkImageAccess
    {
        private readonly byte[] _bytes;
        private readonly int _width;
        private readonly int _height;

        public PresentAccess(byte[] bytes, int width, int height)
        {
            _bytes = bytes;
            _width = width;
            _height = height;
        }

        public Task<ArtworkImageAccessResult> AccessAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkImageAccessResult.Present(
                _bytes,
                _width,
                _height,
                SourceOrientation.TopLeft,
                DateTimeOffset.UnixEpoch,
                null));
        }
    }

    private sealed class PresentReader : IArtworkSourceReader
    {
        private static readonly byte[] Source = PngBytes(1);

        public Task<ArtworkSourceReadResult> ReadAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkSourceReadResult.Present(
                surface,
                "image/png",
                Source,
                ArtworkHashes.ComputeSha256(Source),
                100,
                150,
                DateTimeOffset.UnixEpoch,
                "tag-1"));
        }
    }

    private sealed class NeverCalledWriter : IArtworkImageWriter
    {
        public Task<ArtworkImageMutationResult> SaveImageAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            ReadOnlyMemory<byte> content,
            string contentType,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The publication must be rejected before any image mutation.");
        }

        public Task<ArtworkImageMutationResult> PersistItemUpdateAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The publication must be rejected before any item update.");
        }

        public Task<ArtworkImageMutationResult> RemoveImageAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The publication must be rejected before any image removal.");
        }
    }
}
