using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.PluginLifecycle;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.State;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 restart coverage for the complete Phase 6 pipeline. Every test
/// rebuilds the whole in-memory service graph over the same durable state
/// directory (<see cref="Phase6Harness.Restart"/>) and asserts the documented
/// guarded outcome: metadata state and freshness, published artwork ownership and
/// provenance, non-terminal operation recovery before new work, and bounded
/// retention all survive and do not rely on in-memory state. No live Jellyfin or
/// Arr instance is required.
/// </summary>
public sealed class Phase6RestartTests
{
    private static readonly ArtworkImageSurface Surface = Phase6Harness.Surface;

    [Fact]
    public async Task RestartReloadsPublishedMetadataAndArtworkWithoutRepublishing()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        before.Host.CurrentBytes = Phase6Harness.Original();

        var first = await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, before.Renderer.Calls);
        Assert.Equal(1, before.Host.SaveCalls);

        var metadataBefore = before.Metadata.Read(before.ItemId, ArrProviderKind.Radarr).Value!;
        var stateBefore = before.States.Read(before.ItemId, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, stateBefore.State);
        Assert.True(metadataBefore.IsUsableAsCurrent(DateTimeOffset.UtcNow));

        // Rebuild every in-memory service over the same durable directory.
        using var after = before.Restart();

        var metadataAfter = after.Metadata.Read(after.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, metadataAfter.Status);
        Assert.Equal(metadataBefore.MetadataFingerprint, metadataAfter.Value!.MetadataFingerprint);
        Assert.Equal(metadataBefore.FetchedAt, metadataAfter.Value.FetchedAt);
        Assert.Equal(MetadataFreshness.Fresh, metadataAfter.Value.EvaluateFreshness(DateTimeOffset.UtcNow));

        var stateAfter = after.States.Read(after.ItemId, Surface);
        Assert.Equal(StateReadStatus.Found, stateAfter.Status);
        Assert.Equal(ArtworkPublicationState.Published, stateAfter.Value!.State);
        Assert.Equal(stateBefore.PublishedFingerprint, stateAfter.Value.PublishedFingerprint);
        Assert.Equal(stateBefore.SourceArtifactId, stateAfter.Value.SourceArtifactId);
        Assert.Equal(stateBefore.OwnershipToken, stateAfter.Value.OwnershipToken);
        Assert.Equal(
            SourceArtifactReadStatus.Found,
            after.Artifacts.Read(stateAfter.Value.SourceArtifactId!).Status);

        // The fresh graph drives the unchanged fingerprint to a no-op: no render,
        // no image mutation, no state revision change.
        var second = await after.Recovering.ProcessAsync(after.WorkItem(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(0, after.Renderer.Calls);
        Assert.Equal(0, after.Host.SaveCalls);
        Assert.Equal(stateBefore.StateRevision, after.States.Read(after.ItemId, Surface).Value!.StateRevision);
        Assert.Equal(stateBefore.PublishedFingerprint, after.States.Read(after.ItemId, Surface).Value!.PublishedFingerprint);
    }

    [Fact]
    public async Task RestartRecoversANonTerminalOperationBeforeNewWorkForTheSameSubject()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        var original = Phase6Harness.Original();
        before.Host.CurrentBytes = original;

        // Simulate a crash: the image save completed and the durable journal
        // recorded the repository-update lower bound, but the item-update call
        // threw before the final state was committed.
        before.Host.UpdateHook = _ => throw new InvalidOperationException("Simulated crash during the item update.");

        var crashed = await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        Assert.True(crashed.IsSuccess); // metadata was published before the artwork stage
        var crashedOperation = before.Operations.Read(before.ItemId, Surface).Value!;
        Assert.False(crashedOperation.IsTerminal);
        Assert.Equal(ArtworkOperationPhase.RepositoryUpdateStarted, crashedOperation.Phase);
        Assert.NotEqual(ArtworkHashes.ComputeSha256(original), ArtworkHashes.ComputeSha256(before.Host.CurrentBytes!));
        Assert.Equal(StateReadStatus.Missing, before.States.Read(before.ItemId, Surface).Status);
        Assert.NotNull(crashedOperation.CandidatePublicationFingerprint);
        Assert.NotNull(crashedOperation.SourceArtifactId);

        // Restart over the same durable directory.
        using var after = before.Restart();
        after.Reader.Handler = Phase6Harness.Matched();

        // A probing inner processor proves the recovery gate reached a terminal
        // outcome before any new work was accepted for the subject.
        var spy = new TerminalAwareProcessor(after.Operations, after.ItemId, Surface);
        var decorator = new ArtworkRecoveringWorkItemProcessor(spy, after.Gate);

        var result = await decorator.ProcessAsync(after.WorkItem(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(spy.Called);
        Assert.True(spy.SawTerminalOperation, "The non-terminal operation must be recovered before new work runs.");

        var recovered = after.Operations.Read(after.ItemId, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Committed, recovered.Phase);
        Assert.True(recovered.IsTerminal);

        var committed = after.States.Read(after.ItemId, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, committed.State);
        Assert.Equal(crashedOperation.CandidatePublicationFingerprint, committed.PublishedFingerprint);
        Assert.Equal(
            ArtworkHashes.ComputeSha256(after.Host.CurrentBytes!),
            committed.ActiveImageIdentity!.ContentSha256);
        Assert.Equal(crashedOperation.SourceArtifactId, committed.SourceArtifactId);
        Assert.Equal(SourceArtifactReadStatus.Found, after.Artifacts.Read(committed.SourceArtifactId!).Status);
    }

    [Fact]
    public async Task RestartRetentionPreservesLiveStateAndPrunesExpiredMetadata()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        before.Host.CurrentBytes = Phase6Harness.Original();

        await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        var published = before.States.Read(before.ItemId, Surface).Value!;
        var sourceArtifactId = published.SourceArtifactId!;
        var activeArtifactId = published.ActiveImageIdentity!.ContentSha256!;

        using var after = before.Restart();

        // Seed an expired last-known-good record for an unrelated subject so the
        // restarted retention pass has something safe to prune.
        var expiredItem = Guid.NewGuid();
        var expiredIdentity = ReconciliationFixtures.MovieIdentity(expiredItem);
        var expiredMatch = ReconciliationFixtures.MatchedMovieMatch(expiredIdentity);
        var expiredEntry = MetadataStateEntry.From(
            expiredIdentity,
            expiredMatch,
            ReconciliationFixtures.MovieMetadata(expiredIdentity, expiredMatch.RecordIdentity),
            DateTimeOffset.UtcNow.AddDays(-3));
        after.Metadata.Write(expiredEntry);
        Assert.True(expiredEntry.IsExpired(DateTimeOffset.UtcNow));

        var retention = new StateRetentionService(
            after.Repository,
            after.Metadata,
            new ArtifactRetention(after.Repository, after.Artifacts));

        var removed = retention.RunRetentionPass(DateTimeOffset.UtcNow);

        Assert.True(removed >= 1, "The expired metadata record must be pruned.");

        // The live last-known-good metadata is exempt from render-cache eviction
        // and remains usable as current.
        var liveMetadata = after.Metadata.Read(after.ItemId, ArrProviderKind.Radarr);
        Assert.Equal(StateReadStatus.Found, liveMetadata.Status);
        Assert.True(liveMetadata.Value!.IsUsableAsCurrent(DateTimeOffset.UtcNow));

        // The expired record is gone.
        Assert.Equal(StateReadStatus.Missing, after.Metadata.Read(expiredItem, ArrProviderKind.Radarr).Status);

        // The live ownership session, its retained source baseline, and the active
        // derived image all survive the pass.
        Assert.Equal(ArtworkPublicationState.Published, after.States.Read(after.ItemId, Surface).Value!.State);
        Assert.Equal(SourceArtifactReadStatus.Found, after.Artifacts.Read(sourceArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, after.Artifacts.Read(activeArtifactId).Status);
    }

    [Fact]
    public async Task RestartStartupScanRecoversANonTerminalOperationFromDurableState()
    {
        using var before = new Phase6Harness();
        before.Reader.Handler = Phase6Harness.Matched();
        before.Host.CurrentBytes = Phase6Harness.Original();
        before.Host.UpdateHook = _ => throw new InvalidOperationException("Simulated crash.");
        await before.Recovering.ProcessAsync(before.WorkItem(), CancellationToken.None);

        Assert.Single(before.Operations.Enumerate(10), operation => !operation.IsTerminal);

        using var after = before.Restart();

        // The production startup scan recovers the operation from durable state
        // alone, without any in-memory state from the crashed process.
        var scan = await after.Gate.RecoverStartupAsync(batchSize: 10, CancellationToken.None);

        Assert.Equal(1, scan.Examined);
        Assert.Equal(1, scan.Recovered);
        Assert.Equal(0, scan.AlreadyTerminal);
        Assert.False(scan.ReachedBatchLimit);
        Assert.False(scan.Cancelled);
        Assert.True(scan.IsComplete);
        Assert.DoesNotContain(after.Operations.Enumerate(10), operation => !operation.IsTerminal);
        Assert.Equal(ArtworkPublicationState.Published, after.States.Read(after.ItemId, Surface).Value!.State);
    }

    private sealed class TerminalAwareProcessor : IWorkItemProcessor
    {
        private readonly ArtworkOperationStore _operations;
        private readonly Guid _item;
        private readonly ArtworkImageSurface _surface;

        public TerminalAwareProcessor(ArtworkOperationStore operations, Guid item, ArtworkImageSurface surface)
        {
            _operations = operations;
            _item = item;
            _surface = surface;
        }

        public bool Called { get; private set; }

        public bool SawTerminalOperation { get; private set; }

        public Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
        {
            Called = true;
            SawTerminalOperation = _operations.Read(_item, _surface).Value is { IsTerminal: true };
            return Task.FromResult(WorkProcessingResult.Completed());
        }
    }
}
