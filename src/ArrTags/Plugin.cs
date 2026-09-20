using System;
using System.Threading;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace ArrTags;

/// <summary>
/// The ArrTags plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>
{
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="serviceProvider">The host service provider used to resolve the lifecycle coordinator lazily during uninstall.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IServiceProvider serviceProvider)
        : base(applicationPaths, xmlSerializer)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public override string Name => "ArrTags";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("40322d52-5680-449f-b33e-e01836ee2f46");

    /// <summary>
    /// Called by the host just before the plugin is uninstalled. The pinned host
    /// deletes the plugin data folder immediately after this hook returns, so a
    /// pending drain and guarded restoration must complete here rather than on a
    /// later shutdown. The work is bounded and never throws into the host.
    /// </summary>
    public override void OnUninstalling()
    {
        base.OnUninstalling();
        TryDrainForUninstall();
    }

    private void TryDrainForUninstall()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        IArtworkLifecycleCoordinator coordinator;
        try
        {
            if (_serviceProvider.GetService(typeof(IArtworkLifecycleCoordinator)) is not IArtworkLifecycleCoordinator resolved)
            {
                return;
            }

            coordinator = resolved;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }

        try
        {
            using var bounded = new CancellationTokenSource(ArrTagsLifecycleService.BoundedDrainTimeout);

            // The uninstall hook is synchronous and void, and the host deletes
            // the plugin data folder as soon as it returns. The drain is bounded
            // and all internal await continuations are capture-free, so a
            // bounded synchronous wait is safe and is the only way to restore the
            // retained source before the recovery records are removed.
#pragma warning disable CA1849 // The void uninstall hook cannot await; the wait is bounded.
            coordinator
                .DrainAsync(ArtworkLifecycleFence.Uninstall, bounded.Token)
                .GetAwaiter()
                .GetResult();
#pragma warning restore CA1849
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A blocked or uncertain restoration leaves the image untouched; the
            // host still completes the uninstall.
        }
    }
}
