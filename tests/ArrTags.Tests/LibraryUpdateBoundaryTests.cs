using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.PluginLifecycle;
using ArrTags.Rendering;
using ArrTags.Secrets;
using ArrTags.Updates;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused tests for the task 6.1 library-event entry boundary: relevance
/// validation, the bounded provider-neutral work hint, the non-blocking enqueue
/// boundary, and the evidence that the handlers perform no provider, rendering,
/// or image work and return synchronously. They require no live Jellyfin or Arr
/// instance.
/// </summary>
public class LibraryUpdateBoundaryTests
{
    [Fact]
    public async Task RelevantAddedEventEnqueuesABoundedHintSynchronously()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var coordinator = new RecordingLifecycleCoordinator();
        var configuration = LibraryEventFixtures.CreateConfiguration(sonarrEnabled: true);
        var sink = new RecordingWorkHintSink();
        var service = new ArrTagsLifecycleService(libraryEvents, coordinator, configuration, sink);
        await service.StartAsync(CancellationToken.None);

        var itemId = Guid.NewGuid();
        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(itemId, LibraryWorkReason.Added, MediaItemType.Movie));

        // The handler is synchronous, so the hint is observable immediately with
        // no await or polling.
        var hint = Assert.Single(sink.Hints);
        Assert.Equal(itemId, hint.ItemId);
        Assert.Equal(LibraryWorkReason.Added, hint.Reason);
        Assert.Equal(configuration.Current.ConfigurationVersion, hint.ConfigurationVersion);
        Assert.Equal(0, coordinator.RemovalCalls);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WhenNoProviderIsEnabledNothingIsEnqueued()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var sink = new RecordingWorkHintSink();
        var service = new ArrTagsLifecycleService(
            libraryEvents,
            new RecordingLifecycleCoordinator(),
            LibraryEventFixtures.CreateConfiguration(),
            sink);
        await service.StartAsync(CancellationToken.None);

        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(Guid.NewGuid(), LibraryWorkReason.Added, MediaItemType.Movie));

        Assert.Empty(sink.Hints);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NonBadgeItemTypesAndUnknownTypesAreDropped()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var sink = new RecordingWorkHintSink();
        var service = new ArrTagsLifecycleService(
            libraryEvents,
            new RecordingLifecycleCoordinator(),
            LibraryEventFixtures.CreateConfiguration(sonarrEnabled: true, radarrEnabled: true),
            sink);
        await service.StartAsync(CancellationToken.None);

        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(Guid.NewGuid(), LibraryWorkReason.Added, MediaItemType.Series));
        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(Guid.NewGuid(), LibraryWorkReason.Added, MediaItemType.Season));
        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(Guid.NewGuid(), LibraryWorkReason.Added, itemType: null));

        Assert.Empty(sink.Hints);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ImageOnlyAndEmptyItemChangesAreDropped()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var sink = new RecordingWorkHintSink();
        var service = new ArrTagsLifecycleService(
            libraryEvents,
            new RecordingLifecycleCoordinator(),
            LibraryEventFixtures.CreateConfiguration(radarrEnabled: true),
            sink);
        await service.StartAsync(CancellationToken.None);

        libraryEvents.RaiseUpdated(LibraryEventFixtures.Change(
            Guid.NewGuid(),
            LibraryWorkReason.Updated,
            MediaItemType.Movie,
            LibraryItemChangeOrigin.Image));
        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(Guid.Empty, LibraryWorkReason.Added, MediaItemType.Movie));

        Assert.Empty(sink.Hints);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task EpisodeEventsAreRelevantToSonarrAndRemovalsSurviveAnUnknownType()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var sink = new RecordingWorkHintSink();
        var service = new ArrTagsLifecycleService(
            libraryEvents,
            new RecordingLifecycleCoordinator(),
            LibraryEventFixtures.CreateConfiguration(sonarrEnabled: true),
            sink);
        await service.StartAsync(CancellationToken.None);

        var episodeId = Guid.NewGuid();
        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(episodeId, LibraryWorkReason.Added, MediaItemType.Episode));
        libraryEvents.RaiseRemoved(LibraryEventFixtures.Change(
            Guid.NewGuid(),
            LibraryWorkReason.Removed,
            itemType: null,
            LibraryItemChangeOrigin.Library));

        var hints = sink.Hints;
        Assert.Equal(2, hints.Count);
        Assert.Equal(episodeId, hints[0].ItemId);
        Assert.Equal(LibraryWorkReason.Removed, hints[1].Reason);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ItemRemovedStillDrainsTheLifecycleCoordinator()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var coordinator = new RecordingLifecycleCoordinator();
        var sink = new RecordingWorkHintSink();
        var service = new ArrTagsLifecycleService(
            libraryEvents,
            coordinator,
            LibraryEventFixtures.CreateConfiguration(sonarrEnabled: true),
            sink);
        await service.StartAsync(CancellationToken.None);

        var itemId = Guid.NewGuid();
        libraryEvents.RaiseRemoved(itemId);

        await WaitUntilAsync(() => coordinator.RemovalCalls == 1);
        Assert.Equal(itemId, coordinator.LastRemovedItemId);
        Assert.Single(sink.Hints);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HandlerNeverThrowsWhenTheBoundedSinkIsFull()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var sink = new BoundedWorkHintSink(1);
        var service = new ArrTagsLifecycleService(
            libraryEvents,
            new RecordingLifecycleCoordinator(),
            LibraryEventFixtures.CreateConfiguration(sonarrEnabled: true),
            sink);
        await service.StartAsync(CancellationToken.None);

        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(Guid.NewGuid(), LibraryWorkReason.Added, MediaItemType.Movie));
        libraryEvents.RaiseAdded(LibraryEventFixtures.Change(Guid.NewGuid(), LibraryWorkReason.Added, MediaItemType.Movie));

        Assert.Equal(1, sink.Count);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void BoundedSinkCoalescesDuplicateItemHints()
    {
        var sink = new BoundedWorkHintSink(8);
        var itemId = Guid.NewGuid();

        Assert.True(sink.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Added, 1)));
        Assert.False(sink.TryEnqueue(new LibraryWorkHint(itemId, LibraryWorkReason.Updated, 2)));
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public void BoundedSinkDropsOverflowAndNeverThrows()
    {
        var sink = new BoundedWorkHintSink(2);

        Assert.True(sink.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        Assert.True(sink.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        Assert.False(sink.TryEnqueue(new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1)));
        Assert.Equal(2, sink.Capacity);
        Assert.Equal(2, sink.Count);
    }

    [Fact]
    public void BoundedSinkRejectsAnEmptyItemIdWithoutThrowing()
    {
        var sink = new BoundedWorkHintSink(4);

        Assert.False(sink.TryEnqueue(default));
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void BoundedSinkDequeuesInOrderAndReleasesTheItemForCoalescing()
    {
        var sink = new BoundedWorkHintSink(2);
        var first = new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Added, 1);
        var second = new LibraryWorkHint(Guid.NewGuid(), LibraryWorkReason.Updated, 1);

        Assert.True(sink.TryEnqueue(first));
        Assert.True(sink.TryEnqueue(second));

        Assert.True(sink.TryDequeue(out var dequeuedFirst));
        Assert.Equal(first, dequeuedFirst);

        Assert.True(sink.TryEnqueue(new LibraryWorkHint(first.ItemId, LibraryWorkReason.Removed, 2)));
        Assert.True(sink.TryDequeue(out var dequeuedSecond));
        Assert.Equal(second, dequeuedSecond);
    }

    [Fact]
    public void LibraryEventHandlersHaveNoProviderRendererImageOrSecretDependencies()
    {
        var constructor = Assert.Single(typeof(ArrTagsLifecycleService).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        foreach (var parameterType in parameterTypes)
        {
            var ns = parameterType.Namespace ?? string.Empty;
            Assert.DoesNotContain("ArrTags.Providers", ns, StringComparison.Ordinal);
            Assert.DoesNotContain("ArrTags.Rendering", ns, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(typeof(IRenderer), parameterTypes);
        Assert.DoesNotContain(typeof(IArtworkImageWriter), parameterTypes);
        Assert.DoesNotContain(typeof(IArtworkImageAccess), parameterTypes);
        Assert.DoesNotContain(typeof(IArtworkSourceReader), parameterTypes);
        Assert.DoesNotContain(typeof(IPluginSecretResolver), parameterTypes);
    }

    [Fact]
    public void JellyfinMappingCarriesTheItemTypeAndMarksImageOnlyUpdates()
    {
        var itemId = Guid.NewGuid();
        var movie = new Movie { Id = itemId };

        var added = JellyfinLibraryEventSource.MapChange(
            new ItemChangeEventArgs { Item = movie, UpdateReason = default },
            LibraryWorkReason.Added);
        Assert.Equal(itemId, added.ItemId);
        Assert.Equal(MediaItemType.Movie, added.ItemType);
        Assert.Equal(LibraryItemChangeOrigin.Library, added.Origin);

        var imageUpdate = JellyfinLibraryEventSource.MapChange(
            new ItemChangeEventArgs { Item = movie, UpdateReason = ItemUpdateType.ImageUpdate },
            LibraryWorkReason.Updated);
        Assert.Equal(LibraryItemChangeOrigin.Image, imageUpdate.Origin);

        var series = JellyfinLibraryEventSource.MapChange(
            new ItemChangeEventArgs { Item = new Series { Id = Guid.NewGuid() }, UpdateReason = default },
            LibraryWorkReason.Updated);
        Assert.Equal(MediaItemType.Series, series.ItemType);
    }

    [Fact]
    public void RegistratorRegistersABoundedWorkHintSinkFromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());
        services.AddSingleton<IArtworkLifecycleCoordinator>(new RecordingLifecycleCoordinator());

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IWorkHintSink>();
        var limits = provider.GetRequiredService<ConfigurationSnapshotService>().Current.Limits;

        Assert.Equal(limits.QueueCapacity, sink.Capacity);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private sealed class RecordingLifecycleCoordinator : IArtworkLifecycleCoordinator
    {
        public int RemovalCalls { get; private set; }

        public Guid LastRemovedItemId { get; private set; }

        public void ResetStaleFence()
        {
        }

        public Task<ArtworkLifecycleResult> DrainAsync(
            ArtworkLifecycleFence fence,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkLifecycleResult.Create(
                fence,
                ArtworkLifecycleOutcome.Completed,
                "Drained."));
        }

        public Task<ArtworkLifecycleResult> DrainForHostShutdownAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkLifecycleResult.Create(
                ArtworkLifecycleFence.Normal,
                ArtworkLifecycleOutcome.NothingToDo,
                "No shutdown fence."));
        }

        public Task<ArtworkRemovalResult> HandleItemRemovedAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            RemovalCalls++;
            LastRemovedItemId = itemId;
            return Task.FromResult(ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.NotConfirmed,
                "Not confirmed."));
        }
    }
}
