using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Providers;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 recovery coverage for the complete Phase 6 pipeline: the durable
/// generation fence rejects stale artwork work and only the next generation may
/// supersede a terminal operation, and the durable lifecycle fence rejects a new
/// publication - even when the publication fingerprint changed - without any
/// image mutation, after which the disable drain restores the retained baseline.
/// No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class Phase6RecoveryTests
{
    private static readonly ArtworkImageSurface Surface = Phase6Harness.Surface;

    [Fact]
    public void GenerationFenceRejectsAStaleOperationAndAcceptsOnlyTheNextGeneration()
    {
        using var harness = new Phase6Harness();

        var terminal = ArtworkOperationFixtures.Copy(
            ArtworkOperationFixtures.Publication(item: harness.ItemId, surface: Surface),
            generation: 5,
            phase: ArtworkOperationPhase.Committed);
        harness.Operations.Write(terminal);

        // A stale generation may never overwrite the newer durable record.
        Assert.Throws<InvalidOperationException>(() => harness.Operations.Write(
            ArtworkOperationFixtures.Copy(terminal, generation: 4, phase: ArtworkOperationPhase.Prepared)));

        // A different operation at the same generation is refused: one operation
        // per subject.
        Assert.Throws<InvalidOperationException>(() => harness.Operations.Write(
            ArtworkOperationFixtures.Copy(
                terminal,
                generation: 5,
                phase: ArtworkOperationPhase.Prepared,
                operationId: ArtworkTokens.Create())));

        // Superseding the terminal generation is accepted.
        var next = ArtworkOperationFixtures.Copy(
            terminal,
            generation: 6,
            phase: ArtworkOperationPhase.Prepared,
            operationId: ArtworkTokens.Create());
        harness.Operations.Write(next);

        var stored = harness.Operations.Read(harness.ItemId, Surface).Value!;
        Assert.Equal(6, stored.Generation);
        Assert.False(stored.IsTerminal);
    }

    [Fact]
    public async Task LifecycleFenceRejectsAChangedPublicationWithoutMutationAndTheDrainRestores()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched("Bluray-1080p");
        var original = Phase6Harness.Original();
        harness.Host.CurrentBytes = original;
        await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        var published = harness.States.Read(harness.ItemId, Surface).Value!;
        var activeAfterFirstPublication = harness.Host.CurrentBytes!;
        var saveCalls = harness.Host.SaveCalls;
        var renderCalls = harness.Renderer.Calls;

        // The plugin begins a disable drain.
        harness.Fences.Set(ArtworkLifecycleFence.Disable, "The plugin is being disabled.");

        // A changed metadata fingerprint would normally regenerate, but the fence
        // refuses the new publication before any image mutation.
        harness.Reader.Handler = Phase6Harness.Matched("Bluray-2160p");
        var result = await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(renderCalls + 1, harness.Renderer.Calls);
        Assert.Equal(saveCalls, harness.Host.SaveCalls);

        var after = harness.States.Read(harness.ItemId, Surface).Value!;
        Assert.Equal(published.PublishedFingerprint, after.PublishedFingerprint);
        Assert.Equal(published.StateRevision, after.StateRevision);
        Assert.Equal(activeAfterFirstPublication, harness.Host.CurrentBytes);

        // The disable drain restores the retained original baseline.
        var drain = await harness.Lifecycle.DrainAsync(ArtworkLifecycleFence.Disable, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Completed, drain.Outcome);
        Assert.Equal(ArtworkPublicationState.Restored, harness.States.Read(harness.ItemId, Surface).Value!.State);
        Assert.Equal(original, harness.Host.CurrentBytes);
        Assert.DoesNotContain(harness.Operations.Enumerate(10), operation => !operation.IsTerminal);
    }

    [Fact]
    public async Task CorruptLifecycleFenceFailsClosedTowardRestorationRatherThanPublication()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();
        await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        // A corrupt fence record must fail closed: new publication is refused.
        var fencePath = harness.Repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkLifecycleFenceStore.RecordKind,
            ArtworkLifecycleFenceStore.ActiveRecordId);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fencePath)!);
        await System.IO.File.WriteAllTextAsync(fencePath, "{ this lifecycle fence is corrupt");
        Assert.False(harness.Fences.Read().AllowsNewPublication);

        var saveCalls = harness.Host.SaveCalls;
        var renderCalls = harness.Renderer.Calls;

        harness.Reader.Handler = Phase6Harness.Matched("Bluray-2160p");
        var result = await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(renderCalls + 1, harness.Renderer.Calls);
        Assert.Equal(saveCalls, harness.Host.SaveCalls);

        // The drain resolves the corrupt fence toward uninstall-grade restoration
        // instead of a normal publication window.
        var drain = await harness.Lifecycle.DrainForHostShutdownAsync(CancellationToken.None);
        Assert.Equal(ArtworkLifecycleOutcome.Completed, drain.Outcome);
        Assert.Equal(ArtworkPublicationState.Restored, harness.States.Read(harness.ItemId, Surface).Value!.State);
        Assert.Equal(Phase6Harness.Original(), harness.Host.CurrentBytes);
    }
}
