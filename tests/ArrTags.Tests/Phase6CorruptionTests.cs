using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 corruption coverage for the complete Phase 6 pipeline: a torn,
/// incompatible, or semantically invalid metadata cache is discarded as
/// non-authoritative and rebuilt without blocking startup, while a corrupt
/// authoritative artwork-state or operation record is quarantined and fails
/// closed with no blind replay, no image mutation, and no artifact cleanup. No
/// live Jellyfin or Arr instance is required.
/// </summary>
public sealed class Phase6CorruptionTests
{
    private static readonly ArtworkImageSurface Surface = Phase6Harness.Surface;

    [Fact]
    public async Task TornMetadataCacheIsDiscardedAndRebuiltAfterRestartWithoutBlockingStartup()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        before.Host.CurrentBytes = Phase6Harness.Original();
        await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        var published = before.States.Read(before.ItemId, Surface).Value!;
        var sourceArtifactId = published.SourceArtifactId!;

        // Tear the non-authoritative metadata cache record.
        var recordId = MetadataStateStore.GetRecordId(before.ItemId, ArrProviderKind.Radarr);
        var cachePath = before.Repository.Paths.GetRecordPath(
            StateAuthority.Cache,
            MetadataStateStore.RecordKind,
            recordId);
        await File.WriteAllTextAsync(cachePath, "{ this metadata record is torn");

        using var after = before.Restart();

        var discarded = after.Metadata.Read(after.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.InvalidDiscarded, discarded.Status);
        Assert.False(File.Exists(cachePath));

        // The authoritative ownership state and its retained artifacts are
        // untouched: a corrupt cache never causes cleanup or blind replay.
        Assert.Equal(ArtworkPublicationState.Published, after.States.Read(after.ItemId, Surface).Value!.State);
        Assert.Equal(sourceArtifactId, after.States.Read(after.ItemId, Surface).Value!.SourceArtifactId);
        Assert.Equal(SourceArtifactReadStatus.Found, after.Artifacts.Read(sourceArtifactId).Status);

        // Startup is not blocked: the pipeline rebuilds the cache and the
        // unchanged publication fingerprint produces no redundant render.
        var rebuilt = await after.Recovering.ProcessAsync(after.WorkItem(), CancellationToken.None);

        Assert.True(rebuilt.IsSuccess);
        Assert.Equal(StateReadStatus.Found, after.Metadata.Read(after.ItemId, ArrProviderKind.Radarr).Status);
        Assert.Equal(0, after.Renderer.Calls);
        Assert.Equal(0, after.Host.SaveCalls);
        Assert.Equal(sourceArtifactId, after.States.Read(after.ItemId, Surface).Value!.SourceArtifactId);
    }

    [Fact]
    public async Task IncompatibleAndSemanticallyInvalidMetadataCacheIsDiscardedAfterRestart()
    {
        using var harness = new Phase6Harness();
        var otherItem = Guid.NewGuid();

        // Item 1: a valid record whose envelope schema version is incompatible.
        harness.Metadata.Write(ReconciliationFixtures.MatchedEntry(ReconciliationFixtures.MovieIdentity(harness.ItemId)));
        var firstPath = harness.Repository.Paths.GetRecordPath(
            StateAuthority.Cache,
            MetadataStateStore.RecordKind,
            MetadataStateStore.GetRecordId(harness.ItemId, ArrProviderKind.Radarr));
        var envelope = JsonSerializer.Deserialize<StateEnvelope>(await File.ReadAllBytesAsync(firstPath))!;
        envelope.SchemaVersion = 999;
        AtomicFileWriter.Write(firstPath, JsonSerializer.SerializeToUtf8Bytes(envelope));

        // Item 2: a valid envelope whose payload violates the documented invariants.
        harness.Metadata.Write(ReconciliationFixtures.MatchedEntry(ReconciliationFixtures.MovieIdentity(otherItem)));
        var secondPath = harness.Repository.Paths.GetRecordPath(
            StateAuthority.Cache,
            MetadataStateStore.RecordKind,
            MetadataStateStore.GetRecordId(otherItem, ArrProviderKind.Radarr));
        var invalid = new MetadataStateEntry(
            MetadataStateEntry.CurrentCacheVersion,
            "cache-key",
            otherItem,
            MediaItemType.Movie,
            MediaMatchStatus.Matched,
            MediaMatchMethod.ProviderId,
            "match-fingerprint",
            ArrProviderKind.Radarr,
            ReconciliationFixtures.RadarrConnectionId.Value,
            ReconciliationFixtures.RadarrConnectionId.Value,
            MetadataStateKind.Fresh);
        Assert.False(invalid.Validate(out _));
        AtomicFileWriter.Write(
            secondPath,
            StateEnvelopeCodec.Serialize(
                StateAuthority.Cache,
                MetadataStateStore.RecordKind,
                MetadataStateStore.GetRecordId(otherItem, ArrProviderKind.Radarr),
                invalid,
                DateTimeOffset.UtcNow,
                terminal: false));

        using var after = harness.Restart();

        Assert.Equal(StateReadStatus.InvalidDiscarded, after.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Status);
        Assert.Equal(StateReadStatus.InvalidDiscarded, after.Metadata.Read(otherItem, ArrProviderKind.Radarr).Status);
        Assert.False(File.Exists(firstPath));
        Assert.False(File.Exists(secondPath));

        // Startup is not blocked: an unrelated valid subject still resolves and
        // publishes normally through the restarted graph.
        after.Reader.Handler = Phase6Harness.Matched();
        after.Host.CurrentBytes = Phase6Harness.Original();
        var result = await after.Recovering.ProcessAsync(after.WorkItem(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StateReadStatus.Found, after.Metadata.Read(after.ItemId, ArrProviderKind.Radarr).Status);
    }

    [Fact]
    public async Task CorruptAuthoritativeArtworkStateIsQuarantinedAndThePipelineFailsClosedWithoutCleanup()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        before.Host.CurrentBytes = Phase6Harness.Original();
        await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        var published = before.States.Read(before.ItemId, Surface).Value!;
        var sourceArtifactId = published.SourceArtifactId!;
        var activeArtifactId = published.ActiveImageIdentity!.ContentSha256!;

        var recordId = PublishedArtworkStateStore.GetRecordId(before.ItemId, Surface);
        var statePath = before.Repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            recordId);
        await File.WriteAllTextAsync(statePath, "{ this artwork state is corrupt");

        using var after = before.Restart();

        // The composed pipeline is the first reader of the authoritative state:
        // it fails closed and performs no artwork work or re-baseline.
        var processed = await after.Publishing.ProcessAsync(after.WorkItem(), CancellationToken.None);

        Assert.True(processed.IsSuccess);
        Assert.Equal(0, after.Renderer.Calls);
        Assert.Equal(0, after.Host.SaveCalls);

        // The corrupt record was quarantined, not treated as absent by the
        // failing read, and its bytes are preserved.
        Assert.False(File.Exists(statePath));
        var quarantineDirectory = Path.Combine(after.Root, "quarantine", "authoritative", PublishedArtworkStateStore.RecordKind);
        Assert.True(Directory.Exists(quarantineDirectory));
        Assert.NotEmpty(Directory.GetFiles(quarantineDirectory, "*.json"));

        // No cleanup: the retained artifacts survive the corrupt record.
        Assert.Equal(SourceArtifactReadStatus.Found, after.Artifacts.Read(sourceArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, after.Artifacts.Read(activeArtifactId).Status);
    }

    [Fact]
    public async Task CorruptAuthoritativeArtworkStateBlocksThePublisherWithoutMutation()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        before.Host.CurrentBytes = Phase6Harness.Original();
        await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        var published = before.States.Read(before.ItemId, Surface).Value!;
        var sourceArtifactId = published.SourceArtifactId!;

        var recordId = PublishedArtworkStateStore.GetRecordId(before.ItemId, Surface);
        var statePath = before.Repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            recordId);
        await File.WriteAllTextAsync(statePath, "{ this artwork state is corrupt");

        using var after = before.Restart();

        var candidate = new byte[PipelineArtworkHost.PngSignature.Length + 4];
        PipelineArtworkHost.PngSignature.CopyTo(candidate, 0);
        var candidateHash = ArtworkHashes.ComputeSha256(candidate);
        var request = new ArtworkPublicationRequest(
            before.ItemId,
            Surface,
            RenderResult.Rendered(candidate, 100, 150, candidateHash, candidateHash));

        // The publisher is the first reader of the corrupt authoritative state.
        var publication = await after.Publisher.PublishAsync(request, CancellationToken.None);

        Assert.False(publication.Published);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, publication.Outcome);
        Assert.Equal(0, after.Host.SaveCalls);
        Assert.False(File.Exists(statePath));
        Assert.Equal(SourceArtifactReadStatus.Found, after.Artifacts.Read(sourceArtifactId).Status);
    }

    [Fact]
    public async Task CorruptAuthoritativeOperationRecordIsQuarantinedAndThePipelineFailsClosedWithoutBlindReplay()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        before.Host.CurrentBytes = Phase6Harness.Original();
        await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        var recordId = ArtworkOperationStore.GetRecordId(before.ItemId, Surface);
        var operationPath = before.Repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkOperationStore.RecordKind,
            recordId);
        await File.WriteAllTextAsync(operationPath, "{ this operation record is corrupt");

        using var after = before.Restart();

        // The per-subject recovery gate is the first reader: it quarantines the
        // corrupt record and fails closed, so no new work is accepted.
        var processed = await after.Recovering.ProcessAsync(after.WorkItem(), CancellationToken.None);

        Assert.False(processed.IsSuccess);
        Assert.Equal(0, after.Host.SaveCalls);
        Assert.Equal(0, after.Renderer.Calls);
        Assert.Equal(0, after.Host.RemoveCalls);

        // The corrupt bytes are preserved in quarantine, and no blind replay or
        // cleanup occurs.
        Assert.False(File.Exists(operationPath));
        var quarantineDirectory = Path.Combine(after.Root, "quarantine", "authoritative", ArtworkOperationStore.RecordKind);
        Assert.True(Directory.Exists(quarantineDirectory));
        Assert.NotEmpty(Directory.GetFiles(quarantineDirectory, "*.json"));
    }
}
