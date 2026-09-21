using System;
using System.IO;
using System.Net.Http;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Rendering;
using ArrTags.Secrets;
using ArrTags.State;
using ArrTags.Updates;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// Registers the ArrTags foundation services with Jellyfin. Registration is
/// parameterless and performs no provider, rendering, or full-library work; all
/// background work is owned by the hosted lifecycle service.
/// </summary>
public sealed class ArrTagsServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton(CreateConfigurationSnapshotService);
        serviceCollection.AddSingleton<IPluginSecretResolver>(
            static serviceProvider => serviceProvider.GetRequiredService<ConfigurationSnapshotService>());
        serviceCollection.AddSingleton(CreateStateRepository);
        serviceCollection.TryAddSingleton<ILibraryEventSource, JellyfinLibraryEventSource>();
        serviceCollection.TryAddSingleton<IWorkHintSink>(CreateWorkHintSink);
        serviceCollection.TryAddSingleton<IMediaLibraryResolver, JellyfinMediaLibraryResolver>();
        serviceCollection.TryAddSingleton<IArtworkImageAccess>(CreateArtworkImageAccess);
        serviceCollection.TryAddSingleton<IArtworkSourceReader>(CreateArtworkSourceReader);
        serviceCollection.TryAddSingleton<IArtworkImageWriter>(CreateArtworkImageWriter);
        serviceCollection.TryAddSingleton(CreateSourceArtifactStore);
        serviceCollection.TryAddSingleton(CreatePublishedArtworkStateStore);
        serviceCollection.TryAddSingleton(CreateArtworkOperationStore);
        serviceCollection.TryAddSingleton(CreateArtworkPublisher);
        serviceCollection.TryAddSingleton(CreateArtworkReconciler);
        serviceCollection.TryAddSingleton(CreateArtworkLifecycleFenceStore);
        serviceCollection.TryAddSingleton<IPluginLifecycleFenceProvider>(CreatePluginLifecycleFenceProvider);
        serviceCollection.TryAddSingleton(CreateArtworkLifecycleCoordinator);
        serviceCollection.TryAddSingleton<IArtworkLifecycleCoordinator>(
            static serviceProvider => serviceProvider.GetRequiredService<ArtworkLifecycleCoordinator>());
        serviceCollection.TryAddSingleton<IRenderer>(static _ => new SkiaBadgeRenderer());
        serviceCollection.TryAddSingleton(CreateArtworkGenerationCoordinator);
        RegisterProviderHttpClients(serviceCollection);
        serviceCollection.AddHostedService<ArrTagsLifecycleService>();
    }

    private static void RegisterProviderHttpClients(IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpClient(ArrHttpClientNames.Sonarr);
        serviceCollection.AddHttpClient(ArrHttpClientNames.Radarr);
        serviceCollection
            .AddHttpClient(ArrHttpClientNames.For(ArrProviderKind.Sonarr, ArrTlsPolicy.AllowInsecure))
            .ConfigurePrimaryHttpMessageHandler(CreateInsecureHandler);
        serviceCollection
            .AddHttpClient(ArrHttpClientNames.For(ArrProviderKind.Radarr, ArrTlsPolicy.AllowInsecure))
            .ConfigurePrimaryHttpMessageHandler(CreateInsecureHandler);
        serviceCollection.TryAddSingleton<IArrHttpClientFactory, ArrHttpClientFactory>();
    }

    private static HttpClientHandler CreateInsecureHandler()
    {
#pragma warning disable CA5359 // Relaxed validation is an explicit, validated, connection-scoped opt-in.
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
#pragma warning restore CA5359
    }

    private static ConfigurationSnapshotService CreateConfigurationSnapshotService(IServiceProvider serviceProvider)
    {
        var plugin = FindPlugin(serviceProvider);
        return new ConfigurationSnapshotService(plugin?.Configuration ?? new PluginConfiguration());
    }

    private static StateRepository CreateStateRepository(IServiceProvider serviceProvider)
    {
        var plugin = FindPlugin(serviceProvider);
        var limits = serviceProvider.GetRequiredService<ConfigurationSnapshotService>().Current.Limits;
        var root = string.IsNullOrEmpty(plugin?.DataFolderPath)
            ? Path.Combine(Path.GetTempPath(), "ArrTags")
            : plugin!.DataFolderPath;

        return new StateRepository(root, limits);
    }

    private static BoundedWorkHintSink CreateWorkHintSink(IServiceProvider serviceProvider)
    {
        var capacity = serviceProvider.GetRequiredService<ConfigurationSnapshotService>().Current.Limits.QueueCapacity;
        return new BoundedWorkHintSink(capacity);
    }

    private static JellyfinArtworkImageAccess CreateArtworkImageAccess(IServiceProvider serviceProvider)
    {
        var limits = serviceProvider.GetRequiredService<ConfigurationSnapshotService>().Current.Limits;
        return new JellyfinArtworkImageAccess(
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<IImageProcessor>(),
            limits);
    }

    private static ArtworkSourceReader CreateArtworkSourceReader(IServiceProvider serviceProvider)
    {
        var limits = serviceProvider.GetRequiredService<ConfigurationSnapshotService>().Current.Limits;
        var access = serviceProvider.GetRequiredService<IArtworkImageAccess>();
        return new ArtworkSourceReader(access, limits);
    }

    private static JellyfinArtworkImageWriter CreateArtworkImageWriter(IServiceProvider serviceProvider)
    {
        return new JellyfinArtworkImageWriter(
            serviceProvider.GetRequiredService<ILibraryManager>(),
            serviceProvider.GetRequiredService<IProviderManager>());
    }

    private static SourceArtifactStore CreateSourceArtifactStore(IServiceProvider serviceProvider)
    {
        return new SourceArtifactStore(serviceProvider.GetRequiredService<StateRepository>());
    }

    private static PublishedArtworkStateStore CreatePublishedArtworkStateStore(IServiceProvider serviceProvider)
    {
        return new PublishedArtworkStateStore(serviceProvider.GetRequiredService<StateRepository>());
    }

    private static ArtworkOperationStore CreateArtworkOperationStore(IServiceProvider serviceProvider)
    {
        return new ArtworkOperationStore(serviceProvider.GetRequiredService<StateRepository>());
    }

    private static ArtworkPublisher CreateArtworkPublisher(IServiceProvider serviceProvider)
    {
        var limits = serviceProvider.GetRequiredService<ConfigurationSnapshotService>().Current.Limits;
        return new ArtworkPublisher(
            serviceProvider.GetRequiredService<IArtworkSourceReader>(),
            serviceProvider.GetRequiredService<IArtworkImageWriter>(),
            serviceProvider.GetRequiredService<SourceArtifactStore>(),
            serviceProvider.GetRequiredService<PublishedArtworkStateStore>(),
            serviceProvider.GetRequiredService<ArtworkOperationStore>(),
            limits,
            serviceProvider.GetRequiredService<ArtworkLifecycleFenceStore>());
    }

    private static ArtworkReconciler CreateArtworkReconciler(IServiceProvider serviceProvider)
    {
        return new ArtworkReconciler(
            serviceProvider.GetRequiredService<IArtworkSourceReader>(),
            serviceProvider.GetRequiredService<PublishedArtworkStateStore>(),
            serviceProvider.GetRequiredService<ArtworkOperationStore>(),
            serviceProvider.GetRequiredService<ArtworkPublisher>());
    }

    private static ArtworkLifecycleFenceStore CreateArtworkLifecycleFenceStore(IServiceProvider serviceProvider)
    {
        return new ArtworkLifecycleFenceStore(serviceProvider.GetRequiredService<StateRepository>());
    }

    private static IPluginLifecycleFenceProvider CreatePluginLifecycleFenceProvider(IServiceProvider serviceProvider)
    {
        return new JellyfinPluginLifecycleState(serviceProvider.GetService<IPluginManager>());
    }

    private static ArtworkLifecycleCoordinator CreateArtworkLifecycleCoordinator(IServiceProvider serviceProvider)
    {
        return new ArtworkLifecycleCoordinator(
            serviceProvider.GetRequiredService<IArtworkSourceReader>(),
            serviceProvider.GetRequiredService<PublishedArtworkStateStore>(),
            serviceProvider.GetRequiredService<ArtworkOperationStore>(),
            serviceProvider.GetRequiredService<ArtworkReconciler>(),
            serviceProvider.GetRequiredService<ArtworkPublisher>(),
            serviceProvider.GetRequiredService<ArtworkLifecycleFenceStore>(),
            serviceProvider.GetRequiredService<IPluginLifecycleFenceProvider>());
    }

    private static ArtworkGenerationCoordinator CreateArtworkGenerationCoordinator(IServiceProvider serviceProvider)
    {
        return new ArtworkGenerationCoordinator(
            serviceProvider.GetRequiredService<IArtworkSourceReader>(),
            serviceProvider.GetRequiredService<IRenderer>(),
            serviceProvider.GetRequiredService<ArtworkPublisher>());
    }

    private static Plugin? FindPlugin(IServiceProvider serviceProvider)
    {
        var pluginManager = serviceProvider.GetService<IPluginManager>();
        if (pluginManager is null)
        {
            return null;
        }

        foreach (var localPlugin in pluginManager.Plugins)
        {
            if (localPlugin.Instance is Plugin plugin)
            {
                return plugin;
            }
        }

        return null;
    }
}
