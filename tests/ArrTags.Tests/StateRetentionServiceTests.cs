using System;
using System.IO;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Reconciliation;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 6.5 scheduled retention: the hosted, bounded,
/// cancellation-aware maintenance pass wires the versioned state retention, the
/// metadata freshness retention, and the authoritative artifact GC into
/// production, and metadata last-known-good state is never evicted by the render
/// work-cache policy. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class StateRetentionServiceTests : IDisposable
{
    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly MetadataStateStore _metadata;
    private readonly SourceArtifactStore _artifacts;
    private readonly ArtifactRetention _retention;

    public StateRetentionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-retention-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _metadata = new MetadataStateStore(_repository);
        _artifacts = new SourceArtifactStore(_repository);
        _retention = new ArtifactRetention(_repository, _artifacts, TimeSpan.Zero);
    }

    [Fact]
    public void RunRetentionPassPrunesExpiredMetadataAndReclaimsOrphanArtifacts()
    {
        _metadata.Write(ExpiredEntry());
        var orphan = _artifacts.Promote(Png(1), "image/png").Info!;
        var service = CreateService(TimeSpan.FromHours(1));

        var removed = service.RunRetentionPass(DateTimeOffset.UtcNow);

        Assert.True(removed >= 2);
        Assert.Equal(
            StateReadStatus.Missing,
            _metadata.Read(ReconciliationFixtures.ItemId, ArrTags.Providers.ArrProviderKind.Radarr).Status);
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(orphan.ArtifactId).Status);
    }

    [Fact]
    public void MetadataStateIsNotEvictedByTheRenderCachePolicy()
    {
        var limited = new StateRepository(_root, new OperationalLimits { RenderCacheTtlMinutes = 1 });
        var metadata = new MetadataStateStore(limited);
        var artifacts = new SourceArtifactStore(limited);
        var service = new StateRetentionService(limited, metadata, new ArtifactRetention(limited, artifacts, TimeSpan.Zero));
        metadata.Write(FreshEntry());

        var recordId = MetadataStateStore.GetRecordId(
            ReconciliationFixtures.ItemId,
            ArrTags.Providers.ArrProviderKind.Radarr);
        var path = limited.Paths.GetRecordPath(StateAuthority.Cache, MetadataStateStore.RecordKind, recordId);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-5));

        service.RunRetentionPass(DateTimeOffset.UtcNow);

        Assert.Equal(StateReadStatus.Found, metadata.Read(recordId).Status);
    }

    [Fact]
    public async Task HostedLoopRunsRetentionAndStopsWithinTheBound()
    {
        var orphan = _artifacts.Promote(Png(2), "image/png").Info!;
        var service = CreateService(TimeSpan.FromMilliseconds(25));

        await service.StartAsync(default);
        await service.StartAsync(default);

        var removed = await WaitForRemovalAsync(orphan.ArtifactId, TimeSpan.FromSeconds(10));
        await service.StopAsync(default);

        Assert.True(removed);
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(orphan.ArtifactId).Status);
    }

    [Fact]
    public async Task StoppingAnUnstartedServiceIsSafe()
    {
        var service = CreateService(TimeSpan.FromHours(1));

        await service.StopAsync(default);
        service.Dispose();
    }

    private StateRetentionService CreateService(TimeSpan interval)
    {
        return new StateRetentionService(
            _repository,
            _metadata,
            _retention,
            interval,
            TimeSpan.FromSeconds(5));
    }

    private async Task<bool> WaitForRemovalAsync(string artifactId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_artifacts.Read(artifactId).Status == SourceArtifactReadStatus.Missing)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    private static MetadataStateEntry ExpiredEntry()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        return MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            DateTimeOffset.UtcNow.AddDays(-3),
            staleWindow: TimeSpan.FromHours(24));
    }

    private static MetadataStateEntry FreshEntry()
    {
        var identity = ReconciliationFixtures.MovieIdentity();
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        return MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            DateTimeOffset.UtcNow,
            staleWindow: TimeSpan.FromHours(24));
    }

    private static byte[] Png(int seed)
    {
        var bytes = new byte[64];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        for (var index = signature.Length; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index + seed) & 0xFF);
        }

        return bytes;
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
