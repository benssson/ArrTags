using System;
using System.IO;
using ArrTags.Configuration;
using ArrTags.State;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
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
        serviceCollection.AddSingleton(CreateStateRepository);
        serviceCollection.TryAddSingleton<ILibraryEventSource, JellyfinLibraryEventSource>();
        serviceCollection.AddHostedService<ArrTagsLifecycleService>();
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
