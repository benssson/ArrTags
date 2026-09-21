using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 cancellation coverage for the complete Phase 6 pipeline: cancellation
/// during a provider read, a render, and the image mutation is honored and can
/// never publish partial metadata, partial publication state, or a partial image.
/// No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class Phase6CancellationTests
{
    private static readonly ArtworkImageSurface Surface = Phase6Harness.Surface;

    [Fact]
    public async Task CancellationDuringAProviderReadPublishesNoState()
    {
        using var harness = new Phase6Harness();
        var reader = new Phase6BlockingReader
        {
            Handler = async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return Phase6Harness.ProviderUnavailable();
            },
        };
        var processor = new MetadataReconciliationProcessor(
            harness.Configuration,
            harness.Library,
            new IArrMetadataReader[] { reader },
            harness.Metadata);

        using var cancellation = new CancellationTokenSource();
        var pending = processor.ReconcileAsync(harness.WorkItem(), cancellation.Token);
        await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Equal(StateReadStatus.Missing, harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Status);
        Assert.Equal(StateReadStatus.Missing, harness.States.Read(harness.ItemId, Surface).Status);
        Assert.Equal(0, harness.Renderer.Calls);
        Assert.Equal(0, harness.Host.SaveCalls);
    }

    [Fact]
    public async Task CancellationDuringARenderPublishesNoArtworkAndNoPartialImage()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        var original = Phase6Harness.Original();
        harness.Host.CurrentBytes = original;
        harness.Renderer.RenderHook = cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken);

        using var cancellation = new CancellationTokenSource();
        var pending = harness.Publishing.ProcessAsync(harness.WorkItem(), cancellation.Token);
        await harness.Renderer.RenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // The metadata publication completed before the artwork stage, but no
        // artwork state, render, or image mutation was produced.
        Assert.Equal(StateReadStatus.Found, harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Status);
        Assert.Equal(StateReadStatus.Missing, harness.States.Read(harness.ItemId, Surface).Status);
        Assert.Equal(0, harness.Renderer.Calls);
        Assert.Equal(0, harness.Host.SaveCalls);
        Assert.Equal(original, harness.Host.CurrentBytes);
    }

    [Fact]
    public async Task CancellationDuringTheImageMutationPublishesNoPartialStateOrImage()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        var original = Phase6Harness.Original();
        harness.Host.CurrentBytes = original;
        harness.Host.SaveHook = cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken);

        using var cancellation = new CancellationTokenSource();
        var pending = harness.Publishing.ProcessAsync(harness.WorkItem(), cancellation.Token);
        await harness.Host.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // The save was attempted, but the exact source bytes were never replaced
        // and no final published state was committed.
        Assert.Equal(1, harness.Host.SaveCalls);
        Assert.Equal(original, harness.Host.CurrentBytes);

        var state = harness.States.Read(harness.ItemId, Surface);
        Assert.True(
            state.Status == StateReadStatus.Missing || state.Value!.State != ArtworkPublicationState.Published,
            "A cancelled publication must not commit a published state.");

        var operation = harness.Operations.Read(harness.ItemId, Surface).Value!;
        Assert.NotEqual(ArtworkOperationPhase.Committed, operation.Phase);

        // The durable intent and the retained source are preserved for a later
        // safe recovery; nothing is deleted.
        Assert.Equal(SourceArtifactReadStatus.Found, harness.Artifacts.Read(operation.SourceArtifactId!).Status);
    }

    [Fact]
    public async Task CancellationBeforeProcessingPublishesNothing()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Recovering.ProcessAsync(harness.WorkItem(), cancellation.Token));

        Assert.Equal(StateReadStatus.Missing, harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Status);
        Assert.Equal(StateReadStatus.Missing, harness.States.Read(harness.ItemId, Surface).Status);
        Assert.Equal(0, harness.Renderer.Calls);
        Assert.Equal(0, harness.Host.SaveCalls);
    }
}
