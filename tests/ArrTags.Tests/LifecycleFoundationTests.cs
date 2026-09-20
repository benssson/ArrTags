using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.State;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Foundation-level checks for the dependency-injection registration and the
/// hosted lifecycle: resolution, startup and shutdown, restart, cancellation,
/// and library-event subscription cleanup. These tests require no live
/// Jellyfin or Arr instance.
/// </summary>
public class LifecycleFoundationTests
{
    [Fact]
    public void RegistratorRegistersFoundationServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());
        services.AddSingleton<IArtworkLifecycleCoordinator>(new FakeArtworkLifecycleCoordinator());

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ConfigurationSnapshotService>());
        Assert.NotNull(provider.GetService<StateRepository>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is ArrTagsLifecycleService);
    }

    [Fact]
    public void RegistratorUsesJellyfinLibraryEventSourceByDefault()
    {
        var services = new ServiceCollection();

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILibraryEventSource));
        Assert.Equal(typeof(JellyfinLibraryEventSource), descriptor.ImplementationType);
    }

    [Fact]
    public void RegistrationPerformsNoLibraryWork()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(libraryEvents);

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public void ProvidersRemainDisabledAfterRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());

        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var snapshot = provider.GetRequiredService<ConfigurationSnapshotService>().Current;

        Assert.False(snapshot.SonarrEnabled);
        Assert.False(snapshot.RadarrEnabled);
    }

    [Fact]
    public async Task StartSubscribesAndStopUnsubscribes()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var service = new ArrTagsLifecycleService(libraryEvents, new FakeArtworkLifecycleCoordinator());

        await service.StartAsync(CancellationToken.None);
        Assert.Equal(1, libraryEvents.AddedSubscriberCount);
        Assert.Equal(1, libraryEvents.UpdatedSubscriberCount);
        Assert.Equal(1, libraryEvents.RemovedSubscriberCount);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public async Task LifecycleCanBeRestarted()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var service = new ArrTagsLifecycleService(libraryEvents, new FakeArtworkLifecycleCoordinator());

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);

        Assert.Equal(3, libraryEvents.SubscriberCount);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public async Task CanceledStartDoesNotSubscribe()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var service = new ArrTagsLifecycleService(libraryEvents, new FakeArtworkLifecycleCoordinator());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync(source.Token));

        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public void PluginUninstallHookResolvesTheCoordinatorAndDrainsTheUninstallFence()
    {
        var coordinator = new FakeArtworkLifecycleCoordinator();
        using var provider = new ServiceCollection()
            .AddSingleton<IArtworkLifecycleCoordinator>(coordinator)
            .BuildServiceProvider();
        var plugin = new Plugin(CreateApplicationPaths(), null!, provider);

        plugin.OnUninstalling();

        Assert.Equal(1, coordinator.DrainCalls);
        Assert.Equal(ArtworkLifecycleFence.Uninstall, coordinator.LastDrainFence);
    }

    [Fact]
    public void PluginUninstallHookContainsACoordinatorFailure()
    {
        var coordinator = new FakeArtworkLifecycleCoordinator { ThrowDrain = true };
        using var provider = new ServiceCollection()
            .AddSingleton<IArtworkLifecycleCoordinator>(coordinator)
            .BuildServiceProvider();
        var plugin = new Plugin(CreateApplicationPaths(), null!, provider);

        // A blocked or failing drain must never escape into the host uninstall.
        plugin.OnUninstalling();

        Assert.Equal(1, coordinator.DrainCalls);
    }

    [Fact]
    public void PluginUninstallHookIsSafeWithoutAResolvableCoordinator()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var plugin = new Plugin(CreateApplicationPaths(), null!, provider);

        plugin.OnUninstalling();
    }

    private static IApplicationPaths CreateApplicationPaths()
    {
        return DispatchProxy.Create<IApplicationPaths, FakeApplicationPaths>();
    }

    [Fact]
    public async Task ItemRemovedHintInvokesTheLifecycleCoordinator()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var coordinator = new FakeArtworkLifecycleCoordinator();
        var service = new ArrTagsLifecycleService(libraryEvents, coordinator);
        await service.StartAsync(CancellationToken.None);

        libraryEvents.RaiseRemoved(Guid.NewGuid());

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (coordinator.RemovalCalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, coordinator.RemovalCalls);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopDrainsThroughTheCoordinator()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var coordinator = new FakeArtworkLifecycleCoordinator();
        var service = new ArrTagsLifecycleService(libraryEvents, coordinator);
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, coordinator.ShutdownDrainCalls);
    }

    [Fact]
    public async Task StopIsBoundedWhenTheCoordinatorDrainBlocks()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var coordinator = new FakeArtworkLifecycleCoordinator { BlockShutdownDrain = true };
        var service = new ArrTagsLifecycleService(libraryEvents, coordinator, TimeSpan.FromMilliseconds(100));
        await service.StartAsync(CancellationToken.None);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(1, coordinator.ShutdownDrainCalls);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "The bounded shutdown drain must not block the host indefinitely.");
    }

    [Fact]
    public async Task StopContainsACoordinatorFailure()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var coordinator = new FakeArtworkLifecycleCoordinator { ThrowShutdownDrain = true };
        var service = new ArrTagsLifecycleService(libraryEvents, coordinator);
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public async Task StopUnsubscribesWhenStopTokenIsAlreadyCanceled()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var service = new ArrTagsLifecycleService(libraryEvents, new FakeArtworkLifecycleCoordinator());
        await service.StartAsync(CancellationToken.None);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await service.StopAsync(source.Token);

        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public async Task DisposeUnsubscribesAfterStart()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var service = new ArrTagsLifecycleService(libraryEvents, new FakeArtworkLifecycleCoordinator());
        await service.StartAsync(CancellationToken.None);

        service.Dispose();

        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public void StateRepositoryResolvesToABoundedRootWithoutAPlugin()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILibraryEventSource>(new FakeLibraryEventSource());
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<StateRepository>();

        Assert.False(string.IsNullOrWhiteSpace(repository.Paths.Root));
    }

    public class FakeApplicationPaths : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_PluginsPath")
            {
                return Path.Combine(Path.GetTempPath(), "arrtags-plugin");
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class FakeArtworkLifecycleCoordinator : IArtworkLifecycleCoordinator
    {
        public int ResetCalls { get; private set; }

        public int DrainCalls { get; private set; }

        public int ShutdownDrainCalls { get; private set; }

        public int RemovalCalls { get; private set; }

        public ArtworkLifecycleFence LastDrainFence { get; private set; }

        public bool BlockShutdownDrain { get; set; }

        public bool ThrowShutdownDrain { get; set; }

        public bool ThrowDrain { get; set; }

        public void ResetStaleFence()
        {
            ResetCalls++;
        }

        public Task<ArtworkLifecycleResult> DrainAsync(
            ArtworkLifecycleFence fence,
            CancellationToken cancellationToken)
        {
            DrainCalls++;
            LastDrainFence = fence;
            if (ThrowDrain)
            {
                throw new InvalidOperationException("The fake drain failed.");
            }

            return Task.FromResult(ArtworkLifecycleResult.Create(
                fence,
                ArtworkLifecycleOutcome.Completed,
                "Drained."));
        }

        public async Task<ArtworkLifecycleResult> DrainForHostShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownDrainCalls++;
            if (ThrowShutdownDrain)
            {
                throw new InvalidOperationException("The fake drain failed.");
            }

            if (BlockShutdownDrain)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            return ArtworkLifecycleResult.Create(
                ArtworkLifecycleFence.Normal,
                ArtworkLifecycleOutcome.NothingToDo,
                "No shutdown fence.");
        }

        public Task<ArtworkRemovalResult> HandleItemRemovedAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            RemovalCalls++;
            return Task.FromResult(ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.NotConfirmed,
                "Not confirmed."));
        }
    }

    private sealed class FakeLibraryEventSource : ILibraryEventSource
    {
        private EventHandler<LibraryItemChangedEventArgs>? _added;
        private EventHandler<LibraryItemChangedEventArgs>? _updated;
        private EventHandler<LibraryItemChangedEventArgs>? _removed;

        public int AddedSubscriberCount { get; private set; }

        public int UpdatedSubscriberCount { get; private set; }

        public int RemovedSubscriberCount { get; private set; }

        public int SubscriberCount => AddedSubscriberCount + UpdatedSubscriberCount + RemovedSubscriberCount;

        public void RaiseRemoved(Guid itemId)
        {
            _removed?.Invoke(this, new LibraryItemChangedEventArgs(itemId));
        }

        public event EventHandler<LibraryItemChangedEventArgs>? ItemAdded
        {
            add
            {
                AddedSubscriberCount++;
                _added += value;
            }

            remove
            {
                AddedSubscriberCount--;
                _added -= value;
            }
        }

        public event EventHandler<LibraryItemChangedEventArgs>? ItemUpdated
        {
            add
            {
                UpdatedSubscriberCount++;
                _updated += value;
            }

            remove
            {
                UpdatedSubscriberCount--;
                _updated -= value;
            }
        }

        public event EventHandler<LibraryItemChangedEventArgs>? ItemRemoved
        {
            add
            {
                RemovedSubscriberCount++;
                _removed += value;
            }

            remove
            {
                RemovedSubscriberCount--;
                _removed -= value;
            }
        }
    }
}
