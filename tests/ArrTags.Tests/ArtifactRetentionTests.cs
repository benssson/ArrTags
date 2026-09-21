using System;
using System.IO;
using System.Text;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 6.5 bounded authoritative artifact retention/GC:
/// only artifacts proven non-active and not referenced by a live session source
/// or a non-terminal (or recovery-blocked) operation are removed, authoritative
/// storage is never evicted as ordinary cache, and superseded render output is
/// reclaimed so the quota can be reused. No live Jellyfin or Arr instance is
/// required.
/// </summary>
public sealed class ArtifactRetentionTests : IDisposable
{
    private static readonly Guid Item = Guid.Parse("12345678-1234-1234-1234-1234567890ab");

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly ArtifactRetention _retention;

    public ArtifactRetentionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-artifact-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        _retention = new ArtifactRetention(_repository, _states, _operations, _artifacts, TimeSpan.Zero);
    }

    [Fact]
    public void RetainsTheActiveImageAndTheRetainedSessionSource()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var (source, active) = PublishState(Item, Png(1, 64), Png(2, 64), now);
        var orphan = _artifacts.Promote(Png(3, 64), "image/png").Info!;

        var removed = _retention.Apply(now);

        Assert.Equal(1, removed);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(source.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(active.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(orphan.ArtifactId).Status);
    }

    [Fact]
    public void ReclaimsSupersededDerivedArtifactsButKeepsTheRetainedSource()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var source = _artifacts.Promote(Png(4, 64), "image/png").Info!;
        var superseded = _artifacts.Promote(Png(5, 64), "image/png").Info!;
        WriteState(Item, source, superseded, now);
        var current = _artifacts.Promote(Png(6, 64), "image/png").Info!;
        WriteState(Item, source, current, now);

        var removed = _retention.Apply(now);

        Assert.Equal(1, removed);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(source.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(current.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(superseded.ArtifactId).Status);
    }

    [Fact]
    public void RetainsArtifactsReferencedByANonTerminalOperation()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var source = _artifacts.Promote(Png(8, 64), "image/png").Info!;
        var derived = _artifacts.Promote(Png(9, 64), "image/png").Info!;
        _operations.Write(PublicationOperation(
            Guid.NewGuid(),
            source.ArtifactId,
            derived.ArtifactId,
            ArtworkOperationPhase.Prepared,
            now));

        var removed = _retention.Apply(now);

        Assert.Equal(0, removed);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(source.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(derived.ArtifactId).Status);
    }

    [Fact]
    public void RetainsArtifactsReferencedByARecoveryBlockedOperation()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var source = _artifacts.Promote(Png(10, 64), "image/png").Info!;
        var derived = _artifacts.Promote(Png(11, 64), "image/png").Info!;
        _operations.Write(PublicationOperation(
            Guid.NewGuid(),
            source.ArtifactId,
            derived.ArtifactId,
            ArtworkOperationPhase.RecoveryBlocked,
            now));

        var removed = _retention.Apply(now);

        Assert.Equal(0, removed);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(derived.ArtifactId).Status);
    }

    [Fact]
    public void TerminalProvenanceKeepsItsSourceForTheRetentionWindow()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var source = _artifacts.Promote(Png(12, 64), "image/png").Info!;
        var derived = _artifacts.Promote(Png(13, 64), "image/png").Info!;
        _operations.Write(PublicationOperation(
            Guid.NewGuid(),
            source.ArtifactId,
            derived.ArtifactId,
            ArtworkOperationPhase.Committed,
            now));

        var removed = _retention.Apply(now);

        // The terminal source baseline is retained for the window; the
        // superseded derived output is not the active image and is reclaimed.
        Assert.Equal(1, removed);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(source.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(derived.ArtifactId).Status);
    }

    [Fact]
    public void TerminalProvenanceSourceIsReleasedAfterTheRetentionWindow()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var source = _artifacts.Promote(Png(14, 64), "image/png").Info!;
        var derived = _artifacts.Promote(Png(15, 64), "image/png").Info!;
        _operations.Write(PublicationOperation(
            Guid.NewGuid(),
            source.ArtifactId,
            derived.ArtifactId,
            ArtworkOperationPhase.Committed,
            now));

        Assert.Equal(1, _retention.Apply(now));

        var removedAfterRetention = _retention.Apply(now.AddDays(31));

        Assert.Equal(1, removedAfterRetention);
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(source.ArtifactId).Status);
    }

    [Fact]
    public void FailsClosedWhenAnAuthoritativeRecordCannotBeValidated()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var orphan = _artifacts.Promote(Png(16, 64), "image/png").Info!;
        var corruptPath = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            "corrupt-record");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
        File.WriteAllText(corruptPath, "{ not valid json");

        var removed = _retention.Apply(now);

        Assert.Equal(0, removed);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(orphan.ArtifactId).Status);
    }

    [Fact]
    public void DeletesOrphanArtifactBytesAndManifest()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var orphan = _artifacts.Promote(Png(17, 64), "image/png").Info!;
        var manifestPath = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            SourceArtifactStore.ManifestKind,
            orphan.ArtifactId);
        Assert.True(File.Exists(manifestPath));

        var removed = _retention.Apply(now);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(manifestPath));
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(orphan.ArtifactId).Status);
    }

    [Fact]
    public void ReclaimsQuotaOnRepeatedPublication()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var limited = new StateRepository(_root, new OperationalLimits { ArtifactStorageQuotaBytes = 3 * 64 });
        var artifacts = new SourceArtifactStore(limited);
        var states = new PublishedArtworkStateStore(limited);
        var operations = new ArtworkOperationStore(limited);
        var retention = new ArtifactRetention(limited, states, operations, artifacts, TimeSpan.Zero);

        var source = artifacts.Promote(Png(18, 64), "image/png").Info!;
        var active = artifacts.Promote(Png(19, 64), "image/png").Info!;
        var superseded = artifacts.Promote(Png(20, 64), "image/png").Info!;

        // The quota is full; a further derived artifact is rejected and the
        // current artwork is preserved.
        Assert.False(artifacts.Promote(Png(21, 64), "image/png").Succeeded);

        WriteState(states, Item, source, active, now);
        Assert.Equal(1, retention.Apply(now));
        Assert.Equal(SourceArtifactReadStatus.Missing, artifacts.Read(superseded.ArtifactId).Status);

        var reclaimed = artifacts.Promote(Png(21, 64), "image/png");

        Assert.True(reclaimed.Succeeded);
        Assert.Equal(SourceArtifactReadStatus.Found, artifacts.Read(source.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, artifacts.Read(active.ArtifactId).Status);
    }

    [Fact]
    public void FreshlyPromotedArtifactIsRetainedUntilTheBoundedGraceEnds()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var orphan = _artifacts.Promote(Png(24, 64), "image/png").Info!;
        var retention = new ArtifactRetention(
            _repository,
            _states,
            _operations,
            _artifacts,
            TimeSpan.FromHours(1));

        Assert.Equal(0, retention.Apply(now));
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(orphan.ArtifactId).Status);

        Assert.Equal(1, retention.Apply(now.AddHours(2)));
        Assert.Equal(SourceArtifactReadStatus.Missing, _artifacts.Read(orphan.ArtifactId).Status);
    }

    [Fact]
    public void RenderCacheRetentionNeverTouchesArtifacts()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var (source, active) = PublishState(Item, Png(22, 64), Png(23, 64), now);

        _repository.ApplyRetention(now.AddYears(10));

        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(source.ArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(active.ArtifactId).Status);
    }

    private (SourceArtifactInfo Source, SourceArtifactInfo Active) PublishState(
        Guid itemId,
        byte[] sourceBytes,
        byte[] activeBytes,
        DateTimeOffset at)
    {
        var source = _artifacts.Promote(sourceBytes, "image/png").Info!;
        var active = _artifacts.Promote(activeBytes, "image/png").Info!;
        WriteState(itemId, source, active, at);
        return (source, active);
    }

    private void WriteState(Guid itemId, SourceArtifactInfo source, byte[] activeBytes, DateTimeOffset at)
    {
        var active = _artifacts.Promote(activeBytes, "image/png").Info!;
        WriteState(itemId, source, active, at);
    }

    private void WriteState(Guid itemId, SourceArtifactInfo source, SourceArtifactInfo active, DateTimeOffset at)
    {
        WriteState(_states, itemId, source, active, at);
    }

    private static void WriteState(
        PublishedArtworkStateStore states,
        Guid itemId,
        SourceArtifactInfo source,
        SourceArtifactInfo active,
        DateTimeOffset at)
    {
        var surface = ArtworkImageSurface.Primary;
        var capture = new ActiveImageIdentity(
            surface,
            ArtworkImagePresence.Present,
            source.Sha256,
            source.ByteLength,
            100,
            150,
            DateTimeOffset.UnixEpoch,
            "source-tag");
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, itemId, surface, capture, source, at)
            .State;
        var activeIdentity = new ActiveImageIdentity(
            surface,
            ArtworkImagePresence.Present,
            active.Sha256,
            active.ByteLength,
            100,
            150,
            DateTimeOffset.UnixEpoch,
            "active-tag");
        var committed = PublishedArtworkStateTransitions
            .CommitPublication(
                session,
                activeIdentity,
                ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("published-fingerprint")),
                2,
                at)
            .State;
        states.Write(committed);
    }

    private static ArtworkOperation PublicationOperation(
        Guid itemId,
        string sourceArtifactId,
        string derivedArtifactId,
        ArtworkOperationPhase phase,
        DateTimeOffset at)
    {
        return new ArtworkOperation(
            ArtworkTokens.Create(),
            ArtworkOperationKind.Publication,
            itemId,
            ArtworkImageSurface.Primary,
            1,
            ArtworkStateFixtures.Present(ArtworkImageSurface.Primary, "before-bytes"),
            ArtworkImagePresence.Present,
            ArtworkImagePresence.Present,
            phase,
            ArtworkLifecycleFence.Normal,
            at,
            at,
            ownershipToken: ArtworkTokens.Create(),
            publicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: derivedArtifactId,
            sourceArtifactId: sourceArtifactId,
            derivedArtifactId: derivedArtifactId,
            candidatePublicationFingerprint: ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("publication-fingerprint")),
            rendererVersion: 1);
    }

    private static byte[] Png(int seed, int length)
    {
        var bytes = new byte[Math.Max(length, 8)];
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
