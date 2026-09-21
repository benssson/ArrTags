using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 6.8 provider-outage coverage for the complete Phase 6 pipeline: a
/// temporary provider outage beyond the pending work retries keeps bounded
/// last-known-good metadata only within the configured window and leaves the
/// current usable artwork unchanged; once the window ends the expired snapshot is
/// neither kept nor used to publish. No live Jellyfin or Arr instance is required.
/// </summary>
public sealed class Phase6OutageTests
{
    private static readonly ArtworkImageSurface Surface = Phase6Harness.Surface;

    [Fact]
    public async Task TransientOutageKeepsBoundedLastKnownGoodAndLeavesArtworkUnchanged()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();
        await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        var metadata = harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Value!;
        var published = harness.States.Read(harness.ItemId, Surface).Value!;
        var artworkAfterPublication = harness.Host.CurrentBytes!;
        var saveCalls = harness.Host.SaveCalls;
        var renderCalls = harness.Renderer.Calls;

        // The provider is temporarily unavailable.
        harness.Reader.Result = Phase6Harness.ProviderUnavailable();

        var outage = await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        Assert.False(outage.IsSuccess);
        Assert.True(outage.IsRetryable);

        // The last-known-good snapshot is kept as explicit stale state, and its
        // bounded window is not extended.
        var stale = harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Value!;
        Assert.Equal(MetadataStateKind.Stale, stale.State);
        Assert.Equal(metadata.MetadataFingerprint, stale.MetadataFingerprint);
        Assert.Equal(metadata.ExpiresAt, stale.ExpiresAt);
        Assert.Equal(metadata.StaleUntil, stale.StaleUntil);
        Assert.True(stale.IsUsableAsCurrent(DateTimeOffset.UtcNow));
        Assert.Equal(MetadataFreshness.Stale, stale.EvaluateFreshness(DateTimeOffset.UtcNow));

        // The current usable artwork is left byte-for-byte unchanged and no
        // publication state is modified.
        Assert.Equal(saveCalls, harness.Host.SaveCalls);
        Assert.Equal(renderCalls, harness.Renderer.Calls);
        Assert.Equal(artworkAfterPublication, harness.Host.CurrentBytes);
        var after = harness.States.Read(harness.ItemId, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, after.State);
        Assert.Equal(published.PublishedFingerprint, after.PublishedFingerprint);
        Assert.Equal(published.StateRevision, after.StateRevision);
    }

    [Fact]
    public async Task OutageAfterTheStaleWindowDoesNotKeepOrUseExpiredMetadataAndLeavesArtworkUnchanged()
    {
        using var harness = new Phase6Harness();
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();
        await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        var published = harness.States.Read(harness.ItemId, Surface).Value!;
        var artworkAfterPublication = harness.Host.CurrentBytes!;
        var saveCalls = harness.Host.SaveCalls;
        var renderCalls = harness.Renderer.Calls;

        // The bounded last-known-good window ended before the outage.
        var identity = ReconciliationFixtures.MovieIdentity(harness.ItemId);
        var match = ReconciliationFixtures.MatchedMovieMatch(identity);
        var expired = MetadataStateEntry.From(
            identity,
            match,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            DateTimeOffset.UtcNow.AddDays(-3));
        Assert.True(expired.IsExpired(DateTimeOffset.UtcNow));
        harness.Metadata.Write(expired);

        harness.Reader.Result = Phase6Harness.ProviderUnavailable();

        var outage = await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        Assert.True(outage.IsRetryable);

        // The expired snapshot is neither kept nor refreshed as current.
        var after = harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Value!;
        Assert.Equal(expired.MetadataFingerprint, after.MetadataFingerprint);
        Assert.False(after.IsUsableAsCurrent(DateTimeOffset.UtcNow));
        Assert.Equal(MetadataFreshness.Expired, after.EvaluateFreshness(DateTimeOffset.UtcNow));

        // No publication from expired metadata: the current usable artwork is
        // unchanged and no render or image mutation occurs.
        Assert.Equal(saveCalls, harness.Host.SaveCalls);
        Assert.Equal(renderCalls, harness.Renderer.Calls);
        Assert.Equal(artworkAfterPublication, harness.Host.CurrentBytes);
        Assert.Equal(published.PublishedFingerprint, harness.States.Read(harness.ItemId, Surface).Value!.PublishedFingerprint);

        // The same guard the pipeline uses refuses to generate from the expired
        // metadata: the planner skips an unusable snapshot.
        var snapshot = harness.Configuration.Current;
        var decision = ArtworkRegenerationPlanner.Decide(
            published,
            identity,
            ReconciliationFixtures.MovieMetadata(identity, match.RecordIdentity),
            metadataUsable: false,
            snapshot.BadgeDefinitions,
            snapshot.RendererOutputPolicy,
            snapshot.RendererConfigurationFingerprint,
            RenderVersion.CurrentRendererVersion,
            RenderVersion.CurrentBadgeSchemaVersion);

        Assert.False(decision.ShouldGenerate);
    }

    [Fact]
    public async Task OutageBeyondThePendingWorkRetriesFlagsTheLastKnownGoodAsBoundedStale()
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = true,
            Radarr = new ArrConnectionConfiguration
            {
                Enabled = true,
                BaseUrl = "http://radarr.test",
                ApiKey = "test-radarr-api-key",
            },
        };
        configuration.Limits.TransientRetryCount = 1;
        configuration.Limits.RetryBackoffInitialSeconds = 1;
        configuration.Limits.RetryBackoffFactor = 1;
        configuration.Limits.RetryBackoffMaxSeconds = 1;

        using var harness = new Phase6Harness(configuration: configuration);
        harness.Reader.Handler = Phase6Harness.Matched();
        harness.Host.CurrentBytes = Phase6Harness.Original();
        await harness.Recovering.ProcessAsync(harness.WorkItem(), CancellationToken.None);

        var metadata = harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Value!;
        var artworkAfterPublication = harness.Host.CurrentBytes!;
        var saveCalls = harness.Host.SaveCalls;
        var renderCalls = harness.Renderer.Calls;

        var reads = 0;
        harness.Reader.OnRead = () => Interlocked.Increment(ref reads);
        harness.Reader.Result = Phase6Harness.ProviderUnavailable();

        using var worker = harness.CreateWorker(workerCount: 1, shutdownTimeout: TimeSpan.FromSeconds(2));
        await worker.StartAsync(CancellationToken.None);
        Assert.True(harness.Queue.TryEnqueue(harness.Hint()));

        // One initial attempt plus one bounded retry, then the worker abandons
        // the transient outage.
        await Phase6Wait.UntilAsync(() => Volatile.Read(ref reads) >= 2 && harness.Queue.InFlightCount == 0);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, Volatile.Read(ref reads));

        var stale = harness.Metadata.Read(harness.ItemId, ArrProviderKind.Radarr).Value!;
        Assert.Equal(MetadataStateKind.Stale, stale.State);
        Assert.Equal(metadata.MetadataFingerprint, stale.MetadataFingerprint);
        Assert.Equal(metadata.ExpiresAt, stale.ExpiresAt);
        Assert.Equal(metadata.StaleUntil, stale.StaleUntil);

        // The current artwork is untouched across the whole outage.
        Assert.Equal(saveCalls, harness.Host.SaveCalls);
        Assert.Equal(renderCalls, harness.Renderer.Calls);
        Assert.Equal(artworkAfterPublication, harness.Host.CurrentBytes);
    }
}
