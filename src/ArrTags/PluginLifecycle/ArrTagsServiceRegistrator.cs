using System;
using System.IO;
using System.Net.Http;
using ArrTags.Artwork;
using ArrTags.Concurrency;
using ArrTags.Configuration;
using ArrTags.Logging;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Providers.Radarr;
using ArrTags.Providers.Sonarr;
using ArrTags.Reconciliation;
using ArrTags.Rendering;
using ArrTags.Secrets;
using ArrTags.State;
using ArrTags.Updates;
using ArrTags.Webhooks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

        // ADR-020 clause 3: per-plugin verbosity is a plugin-owned gate over the
        // current configuration snapshot. It resolves ILogger<T>/ILoggerFactory
        // through the host DI; ArrTags registers no custom ILoggerProvider/sink
        // and does not replace the host ILoggerFactory.
        serviceCollection.TryAddSingleton<ILogVerbosityGate, LogVerbosityGate>();

        // ADR-020 clauses 4 and 6: the plugin-owned logging boundary resolves the
        // host ILogger<T> per instrumented type, gates on the verbosity gate, and
        // bounds volume with one shared repetition suppressor. Registration never
        // fails when the host has not registered logging (a NullLogger fallback is
        // used), and no custom ILoggerProvider/sink is added.
        RegisterArrTagsLogs(serviceCollection);

        // ADR-021: the administrator-visible rejection surfacing is isolated
        // behind the plugin-owned IConfigurationRejectionNotifier boundary; the
        // Jellyfin implementation resolves the host IActivityManager lazily and
        // never throws into the host.
        serviceCollection.TryAddSingleton<IConfigurationRejectionNotifier>(
            static serviceProvider => new JellyfinConfigurationRejectionNotifier(serviceProvider));
        serviceCollection.AddSingleton(CreateStateRepository);
        serviceCollection.TryAddSingleton<ILibraryEventSource, JellyfinLibraryEventSource>();
        serviceCollection.TryAddSingleton(CreateLibraryWorkQueue);
        serviceCollection.TryAddSingleton<IWorkHintSink>(
            static serviceProvider => serviceProvider.GetRequiredService<LibraryWorkQueue>());
        serviceCollection.TryAddSingleton<IMediaLibraryResolver, JellyfinMediaLibraryResolver>();
        serviceCollection.TryAddSingleton<IMediaLibraryEnumerator, JellyfinMediaLibraryEnumerator>();
        serviceCollection.TryAddSingleton(CreateMetadataStateStore);
        serviceCollection.TryAddSingleton<IArrReadClientFactory, ArrReadClientFactory>();

        // ADR-018: the bounded provider inventory cache is resolved from the
        // current configuration snapshot so a replaced limits snapshot takes
        // effect without rebuilding the singleton; the provider-neutral metadata
        // readers populate and consume it at the provider boundary.
        serviceCollection.TryAddSingleton<ArrInventoryCacheProvider>();
        serviceCollection.TryAddSingleton<ProviderConcurrencyLimiter>();
        serviceCollection.TryAddSingleton<RadarrMetadataReader>();
        serviceCollection.TryAddSingleton<SonarrMetadataReader>();
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IArrMetadataReader, ConcurrencyLimitedArrMetadataReader<RadarrMetadataReader>>());
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IArrMetadataReader, ConcurrencyLimitedArrMetadataReader<SonarrMetadataReader>>());
        serviceCollection.TryAddSingleton<MetadataReconciliationProcessor>();
        serviceCollection.TryAddSingleton(CreateArtworkRecoveryGate);
        serviceCollection.TryAddSingleton<IArtworkRecoveryGate>(
            static serviceProvider => serviceProvider.GetRequiredService<ArtworkRecoveryGate>());
        serviceCollection.TryAddSingleton(CreateArtworkPublishingWorkItemProcessor);
        serviceCollection.TryAddSingleton<IWorkItemProcessor>(CreateArtworkRecoveringWorkItemProcessor);
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
        serviceCollection.TryAddSingleton<IRenderer>(CreateRenderer);
        serviceCollection.TryAddSingleton(CreateArtworkGenerationCoordinator);
        serviceCollection.TryAddSingleton(CreateArtifactRetention);
        serviceCollection.TryAddSingleton(CreateLibraryReconciliationService);

        // ADR-016 clause 5 second bullet: the post-save reconciliation trigger is
        // a plugin-owned boundary so the elevation-gated save path never touches
        // the reconciliation service directly. It is a hosted singleton: the
        // hosted loop runs the existing bounded reconciliation off the save
        // thread, and registration performs no provider, rendering, or library
        // work.
        serviceCollection.TryAddSingleton<ConfigurationReconciliationTrigger>();
        serviceCollection.TryAddSingleton<IConfigurationReconciliationTrigger>(
            static serviceProvider => serviceProvider.GetRequiredService<ConfigurationReconciliationTrigger>());

        // The scheduled and post-scan reconciliation tasks are concrete public
        // types that Jellyfin discovers by scanning the plugin assembly
        // (IScheduledTask via ITaskManager.AddTasks, ILibraryPostScanTask via
        // ILibraryManager.AddParts). They are also registered here so their
        // dependency wiring is explicit and directly resolvable; registration
        // performs no provider, rendering, or library work, and neither task runs
        // until Jellyfin invokes it.
        serviceCollection.TryAddSingleton<ArrTagsReconciliationTask>();
        serviceCollection.TryAddSingleton<ArrTagsPostScanTask>();
        RegisterProviderHttpClients(serviceCollection);
        serviceCollection.AddHostedService<ArrTagsLifecycleService>();

        // The hosted post-save reconciliation loop waits for bounded requests
        // from a successful configuration replacement; it performs no work until
        // one is requested. Registered after the lifecycle service so the fence
        // state is settled before any reconciliation can run.
        serviceCollection.AddHostedService(
            static serviceProvider => serviceProvider.GetRequiredService<ConfigurationReconciliationTrigger>());

        // Registered after the lifecycle service (which clears a stale fence) so
        // the bounded startup scan runs against the active fence, and before the
        // work worker so it starts first. New work is additionally gated per
        // subject, so a scan that does not cover every record is still safe.
        serviceCollection.AddHostedService<ArtworkStartupRecoveryService>();

        // Bounded retention maintenance (state cache/quota, terminal provenance,
        // metadata freshness, and authoritative artifact GC) runs on its own
        // tracked background loop and performs no provider, render, or image work.
        serviceCollection.AddHostedService<StateRetentionService>();

        // Registered after the lifecycle service so a host shutdown stops
        // accepting and cancels queued/in-flight work before the lifecycle drain
        // establishes the durable fence.
        serviceCollection.AddHostedService<LibraryWorkWorker>();

        // The bounded webhook intake and its hosted resolver. Registered after
        // the work worker so the bounded queue is accepting before webhook
        // resolution can enqueue hints. The inbound controller itself is
        // discovered by Jellyfin's plugin controller registration and resolves
        // these services from DI.
        serviceCollection.TryAddSingleton(CreateWebhookReconciliationResolver);
        serviceCollection.TryAddSingleton(static _ => new WebhookIntake());
        serviceCollection.TryAddSingleton<IWebhookIntake>(
            static serviceProvider => serviceProvider.GetRequiredService<WebhookIntake>());
        serviceCollection.AddHostedService<WebhookIntakeService>();
    }

    /// <summary>
    /// Registers the plugin-owned logging boundary for every instrumented
    /// boundary type (ADR-020). Each type gets a category-scoped
    /// <see cref="IArrTagsLog{T}"/> over the host <see cref="ILogger{T}"/>, the
    /// current-snapshot verbosity gate, and one shared <see cref="LogThrottle"/>.
    /// When the host has not registered logging, the category logger falls back
    /// to a <see cref="NullLogger{T}"/>, so registration never fails.
    /// </summary>
    private static void RegisterArrTagsLogs(IServiceCollection serviceCollection)
    {
        serviceCollection.TryAddSingleton<LogThrottle>();
        serviceCollection.TryAddSingleton<IArrTagsLog<ConcurrencyLimitedArrMetadataReader<RadarrMetadataReader>>>(CreateLog<ConcurrencyLimitedArrMetadataReader<RadarrMetadataReader>>);
        serviceCollection.TryAddSingleton<IArrTagsLog<ConcurrencyLimitedArrMetadataReader<SonarrMetadataReader>>>(CreateLog<ConcurrencyLimitedArrMetadataReader<SonarrMetadataReader>>);
        serviceCollection.TryAddSingleton<IArrTagsLog<RadarrMetadataReader>>(CreateLog<RadarrMetadataReader>);
        serviceCollection.TryAddSingleton<IArrTagsLog<SonarrMetadataReader>>(CreateLog<SonarrMetadataReader>);
        serviceCollection.TryAddSingleton<IArrTagsLog<MetadataReconciliationProcessor>>(CreateLog<MetadataReconciliationProcessor>);
        serviceCollection.TryAddSingleton<IArrTagsLog<ArtworkGenerationCoordinator>>(CreateLog<ArtworkGenerationCoordinator>);
        serviceCollection.TryAddSingleton<IArrTagsLog<LibraryWorkWorker>>(CreateLog<LibraryWorkWorker>);
        serviceCollection.TryAddSingleton<IArrTagsLog<LibraryReconciliationService>>(CreateLog<LibraryReconciliationService>);
        serviceCollection.TryAddSingleton<IArrTagsLog<WebhookIntakeService>>(CreateLog<WebhookIntakeService>);
        serviceCollection.TryAddSingleton<IArrTagsLog<WebhookAuthenticationFilter>>(CreateLog<WebhookAuthenticationFilter>);
        serviceCollection.TryAddSingleton<IArrTagsLog<ArrTagsLifecycleService>>(CreateLog<ArrTagsLifecycleService>);
    }

    private static IArrTagsLog<T> CreateLog<T>(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
        return new ArrTagsLog<T>(
            logger,
            serviceProvider.GetRequiredService<ILogVerbosityGate>(),
            serviceProvider.GetRequiredService<LogThrottle>());
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

    private static IRenderer CreateRenderer(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetRequiredService<ConfigurationSnapshotService>();

        // The render limit is resolved from the current snapshot on each render,
        // so a replaced configuration takes effect without rebuilding the
        // singleton (ADR-004).
        var limiter = new DynamicConcurrencyLimiter(
            () => configuration.Current.Limits.RenderConcurrency);
        return new ConcurrencyLimitedRenderer(new SkiaBadgeRenderer(), limiter);
    }

    private static LibraryReconciliationService CreateLibraryReconciliationService(IServiceProvider serviceProvider)
    {
        return new LibraryReconciliationService(
            serviceProvider.GetRequiredService<ConfigurationSnapshotService>(),
            serviceProvider.GetRequiredService<IMediaLibraryResolver>(),
            serviceProvider.GetRequiredService<IMediaLibraryEnumerator>(),
            serviceProvider.GetRequiredService<IWorkHintSink>(),
            serviceProvider.GetRequiredService<ArtworkLifecycleFenceStore>(),
            serviceProvider.GetRequiredService<IArrTagsLog<LibraryReconciliationService>>());
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

    private static LibraryWorkQueue CreateLibraryWorkQueue(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetRequiredService<ConfigurationSnapshotService>();

        // The capacity and per-item in-flight bound are resolved from the
        // current snapshot on each operation, so a replaced configuration takes
        // effect without rebuilding the singleton.
        return new LibraryWorkQueue(() => configuration.Current.Limits);
    }

    private static MetadataStateStore CreateMetadataStateStore(IServiceProvider serviceProvider)
    {
        return new MetadataStateStore(serviceProvider.GetRequiredService<StateRepository>());
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

    private static ArtworkRecoveryGate CreateArtworkRecoveryGate(IServiceProvider serviceProvider)
    {
        return new ArtworkRecoveryGate(
            serviceProvider.GetRequiredService<ArtworkOperationStore>(),
            serviceProvider.GetRequiredService<ArtworkReconciler>(),
            serviceProvider.GetRequiredService<ArtworkLifecycleFenceStore>());
    }

    private static ArtworkPublishingWorkItemProcessor CreateArtworkPublishingWorkItemProcessor(IServiceProvider serviceProvider)
    {
        return new ArtworkPublishingWorkItemProcessor(
            serviceProvider.GetRequiredService<MetadataReconciliationProcessor>(),
            serviceProvider.GetRequiredService<ArtworkGenerationCoordinator>(),
            serviceProvider.GetRequiredService<PublishedArtworkStateStore>(),
            serviceProvider.GetRequiredService<ConfigurationSnapshotService>());
    }

    private static IWorkItemProcessor CreateArtworkRecoveringWorkItemProcessor(IServiceProvider serviceProvider)
    {
        return new ArtworkRecoveringWorkItemProcessor(
            serviceProvider.GetRequiredService<ArtworkPublishingWorkItemProcessor>(),
            serviceProvider.GetRequiredService<IArtworkRecoveryGate>());
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
            serviceProvider.GetRequiredService<ArtworkPublisher>(),
            serviceProvider.GetRequiredService<PublishedArtworkStateStore>(),
            serviceProvider.GetRequiredService<SourceArtifactStore>(),
            serviceProvider.GetRequiredService<IArrTagsLog<ArtworkGenerationCoordinator>>());
    }

    private static ArtifactRetention CreateArtifactRetention(IServiceProvider serviceProvider)
    {
        return new ArtifactRetention(
            serviceProvider.GetRequiredService<StateRepository>(),
            serviceProvider.GetRequiredService<PublishedArtworkStateStore>(),
            serviceProvider.GetRequiredService<ArtworkOperationStore>(),
            serviceProvider.GetRequiredService<SourceArtifactStore>());
    }

    private static WebhookReconciliationResolver CreateWebhookReconciliationResolver(IServiceProvider serviceProvider)
    {
        return new WebhookReconciliationResolver(serviceProvider.GetRequiredService<MetadataStateStore>());
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
