using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace ArrTags;

/// <summary>
/// The ArrTags plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The plugin-owned state folder name. The state root is created directly
    /// under the host program data directory (a sibling of the plugins
    /// directory), never inside the plugins directory, so it can never be
    /// enumerated as a plugin folder.
    /// </summary>
    public const string StateFolderName = "ArrTags";

    /// <summary>
    /// The dashboard settings page name. It is the value of the
    /// <c>name</c> query parameter and the <c>PluginPageInfo.Name</c>.
    /// </summary>
    public const string SettingsPageName = "ArrTags";

    /// <summary>
    /// The exact assembly manifest resource logical name of the embedded
    /// dashboard settings page (ADR-016 clause 1).
    /// </summary>
    public const string SettingsPageResourceName = "ArrTags.Configuration.config.html";

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
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _serviceProvider = serviceProvider;

        // The pinned BasePlugin<T> constructor derives DataFolderPath as
        // Path.Combine(ApplicationPaths.PluginsPath, Path.GetFileNameWithoutExtension(assembly.Location)),
        // i.e. PluginsPath/ArrTags; its "_<version>" branch is dead because
        // Version is still null at that point. Under the supported versioned
        // install layout (PluginsPath/ArrTags_<version>) that derived data folder
        // collides with the install folder in PluginManager discovery: both
        // resolve to the same manifest name and the install folder is deleted on
        // the next host restart. Re-point the data folder at a host-visible but
        // non-plugin location before any state is written. SetAttributes is the
        // public contract method the host loader itself uses (IPluginAssembly).
        var assembly = typeof(Plugin).Assembly;
        SetAttributes(assembly.Location, ResolveStateRootDirectory(applicationPaths), assembly.GetName().Version);
    }

    /// <inheritdoc />
    public override string Name => "ArrTags";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("40322d52-5680-449f-b33e-e01836ee2f46");

    /// <summary>
    /// Returns the single ArrTags dashboard settings page. The page is served by
    /// the pinned host <c>DashboardController</c> from the embedded resource
    /// named by <see cref="SettingsPageResourceName"/>; the host injects nothing
    /// and the page itself embeds no secret (ADR-016 clauses 1, 2, and 6).
    /// </summary>
    /// <returns>The one dashboard settings page.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = SettingsPageName,
            EmbeddedResourcePath = SettingsPageResourceName,
            EnableInMainMenu = false,
        };
    }

    /// <summary>
    /// Resolves the plugin-owned state root. It is placed directly under the
    /// host program data directory and is deliberately outside
    /// <see cref="IApplicationPaths.PluginsPath"/> so plugin discovery never
    /// treats it as a same-named plugin copy.
    /// </summary>
    /// <param name="applicationPaths">The host application paths.</param>
    /// <returns>The absolute plugin state root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="applicationPaths"/> is <see langword="null"/>.</exception>
    public static string ResolveStateRootDirectory(IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        return Path.Combine(applicationPaths.ProgramDataPath, StateFolderName);
    }

    /// <summary>
    /// Called by the host just before the plugin is uninstalled. The uninstall
    /// drain must complete here, before the host completes the uninstall. The
    /// pinned host removes the versioned install folder (not the plugin's
    /// <c>DataFolderPath</c>), so once the bounded drain has resolved every
    /// operation and restoration the plugin removes its own relocated state root
    /// to preserve the previous uninstall cleanup semantics. The work is bounded
    /// and never throws into the host.
    /// </summary>
    public override void OnUninstalling()
    {
        base.OnUninstalling();

        if (TryDrainForUninstall())
        {
            TryDeleteStateRoot();
        }
    }

    /// <summary>
    /// Runs the bounded synchronous uninstall drain.
    /// </summary>
    /// <returns><see langword="true"/> only when the drain completed and every recovery record is resolved.</returns>
    private bool TryDrainForUninstall()
    {
        if (_serviceProvider is null)
        {
            return false;
        }

        IArtworkLifecycleCoordinator coordinator;
        try
        {
            if (_serviceProvider.GetService(typeof(IArtworkLifecycleCoordinator)) is not IArtworkLifecycleCoordinator resolved)
            {
                return false;
            }

            coordinator = resolved;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }

        try
        {
            using var bounded = new CancellationTokenSource(ArrTagsLifecycleService.BoundedDrainTimeout);

            // The uninstall hook is synchronous and void. The drain is bounded
            // and all internal await continuations are capture-free, so a
            // bounded synchronous wait is safe and is the only way to restore the
            // retained source before the recovery records are removed.
#pragma warning disable CA1849 // The void uninstall hook cannot await; the wait is bounded.
            var result = coordinator
                .DrainAsync(ArtworkLifecycleFence.Uninstall, bounded.Token)
                .GetAwaiter()
                .GetResult();
#pragma warning restore CA1849

            return result.IsComplete;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A blocked or uncertain restoration leaves the image untouched and
            // its recovery records retained; the host still completes the
            // uninstall.
            return false;
        }
    }

    /// <summary>
    /// Removes the relocated plugin state root after a completed uninstall
    /// drain. Cleanup is best-effort and bounded: an incomplete drain retains the
    /// recovery records and a removal failure is never thrown into the host.
    /// </summary>
    private void TryDeleteStateRoot()
    {
        var dataFolderPath = DataFolderPath;
        if (string.IsNullOrEmpty(dataFolderPath)
            || !string.Equals(Path.GetFileName(dataFolderPath), StateFolderName, StringComparison.Ordinal))
        {
            // Never delete a folder that is not the plugin's own relocated state
            // root (for example the Jellyfin-derived plugins folder path).
            return;
        }

        try
        {
            if (Directory.Exists(dataFolderPath))
            {
                Directory.Delete(dataFolderPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The uninstalled plugin leaves orphaned state rather than failing
            // the host uninstall.
        }
    }
}
