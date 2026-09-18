using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.State;
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
        var service = new ArrTagsLifecycleService(libraryEvents);

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
        var service = new ArrTagsLifecycleService(libraryEvents);

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
        var service = new ArrTagsLifecycleService(libraryEvents);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync(source.Token));

        Assert.Equal(0, libraryEvents.SubscriberCount);
    }

    [Fact]
    public async Task StopUnsubscribesWhenStopTokenIsAlreadyCanceled()
    {
        var libraryEvents = new FakeLibraryEventSource();
        var service = new ArrTagsLifecycleService(libraryEvents);
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
        var service = new ArrTagsLifecycleService(libraryEvents);
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

    private sealed class FakeLibraryEventSource : ILibraryEventSource
    {
        private EventHandler<LibraryItemChangedEventArgs>? _added;
        private EventHandler<LibraryItemChangedEventArgs>? _updated;
        private EventHandler<LibraryItemChangedEventArgs>? _removed;

        public int AddedSubscriberCount { get; private set; }

        public int UpdatedSubscriberCount { get; private set; }

        public int RemovedSubscriberCount { get; private set; }

        public int SubscriberCount => AddedSubscriberCount + UpdatedSubscriberCount + RemovedSubscriberCount;

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
