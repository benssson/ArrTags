using System;
using ArrTags.Artwork;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The Jellyfin 12 host implementation of the plugin-state fence provider. There
/// is no host <c>OnDisable</c> hook; disabling a plugin writes its manifest
/// status and takes effect on the next restart while the running instance keeps
/// executing. This adapter therefore reports the fence from the plugin manifest
/// status through the supported plugin manager, which the disable and uninstall
/// controllers update in memory immediately. A normal shutdown leaves the status
/// active and reports a normal fence, so it never triggers restoration.
/// </summary>
/// <remarks>
/// A disable that takes effect only after a restart cannot be observed from a
/// disabled, unloaded plugin; the supported trigger is the graceful shutdown of
/// the still-loaded instance, which can still see the persisted disabled status.
/// All Jellyfin references are confined to this file.
/// </remarks>
public sealed class JellyfinPluginLifecycleState : IPluginLifecycleFenceProvider
{
    private readonly IPluginManager? _pluginManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPluginLifecycleState"/> class.
    /// </summary>
    /// <param name="pluginManager">The Jellyfin plugin manager, or <see langword="null"/> when the host does not expose one.</param>
    public JellyfinPluginLifecycleState(IPluginManager? pluginManager)
    {
        _pluginManager = pluginManager;
    }

    /// <inheritdoc />
    public ArtworkLifecycleFence GetLifecycleFence()
    {
        if (_pluginManager is null)
        {
            return ArtworkLifecycleFence.Normal;
        }

        try
        {
            foreach (var plugin in _pluginManager.Plugins)
            {
                if (plugin.Instance is Plugin)
                {
                    return Map(plugin.Manifest.Status);
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkLifecycleFence.Normal;
        }

        return ArtworkLifecycleFence.Normal;
    }

    private static ArtworkLifecycleFence Map(PluginStatus status)
    {
        return status switch
        {
            PluginStatus.Deleted => ArtworkLifecycleFence.Uninstall,
            PluginStatus.Disabled => ArtworkLifecycleFence.Disable,
            _ => ArtworkLifecycleFence.Normal,
        };
    }
}
