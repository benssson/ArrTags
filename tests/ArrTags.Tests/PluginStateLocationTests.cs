using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Regression coverage for task 7.7: the plugin's persisted state root must live
/// outside Jellyfin's plugins directory so the supported versioned install
/// layout (<c>PluginsPath/ArrTags_&lt;version&gt;</c>) is never mistaken for a
/// same-named plugin data folder and deleted on the next host restart.
/// </summary>
public class PluginStateLocationTests
{
    [Fact]
    public void DataFolderIsRelocatedOutsideThePluginsPath()
    {
        var root = CreateRoot();
        try
        {
            var paths = CreateApplicationPaths(root);
            var plugin = new Plugin(paths, null!, null!);

            // The old, Jellyfin-derived data folder lived at PluginsPath/ArrTags.
            var oldDerivedFolder = Path.Combine(paths.PluginsPath, Plugin.StateFolderName);
            Assert.Equal(Path.Combine(root, "plugins", Plugin.StateFolderName), oldDerivedFolder);

            Assert.Equal(Path.Combine(root, Plugin.StateFolderName), plugin.DataFolderPath);
            Assert.NotEqual(oldDerivedFolder, plugin.DataFolderPath);
            Assert.False(
                IsUnder(plugin.DataFolderPath, paths.PluginsPath),
                $"The plugin state root '{plugin.DataFolderPath}' must not be inside the plugins path '{paths.PluginsPath}'.");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void PersistedStateDoesNotCreateASameNamedPluginFolderUnderThePluginsPath()
    {
        var root = CreateRoot();
        try
        {
            // Arrange the supported versioned install layout that Jellyfin's
            // InstallationManager produces.
            var paths = CreateApplicationPaths(root);
            var installFolder = Path.Combine(paths.PluginsPath, "ArrTags_1.0.1.0");
            Directory.CreateDirectory(installFolder);
            File.WriteAllText(Path.Combine(installFolder, "meta.json"), "{}");

            var plugin = new Plugin(paths, null!, null!);

            // Simulate the plugin's first real state write (metadata, artwork
            // operation, lifecycle fence, or source artifact).
            var sentinel = Path.Combine(plugin.DataFolderPath, "authoritative", "sentinel.json");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "{\"kind\":\"fence\"}");

            var discovered = Directory.GetDirectories(paths.PluginsPath);
            Assert.Equal(
                new[] { installFolder },
                discovered.OrderBy(path => path, StringComparer.Ordinal).ToArray());

            // PluginManager.DiscoverPlugins groups directories by manifest name
            // and deletes all but one of the same-named entries. After the state
            // write there must still be exactly one entry per plugin name.
            var names = discovered.Select(DerivePluginName).ToArray();
            Assert.Single(names);
            Assert.Equal(Plugin.StateFolderName, names[0]);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void UninstallHookRemovesTheRelocatedStateRootAfterACompletedDrain()
    {
        var root = CreateRoot();
        try
        {
            var paths = CreateApplicationPaths(root);
            var coordinator = new FakeArtworkLifecycleCoordinator(ArtworkLifecycleOutcome.Completed);
            using var provider = CreateProvider(coordinator);
            var plugin = new Plugin(paths, null!, provider);

            var sentinel = Path.Combine(plugin.DataFolderPath, "authoritative", "sentinel.json");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "{}");

            plugin.OnUninstalling();

            Assert.Equal(1, coordinator.DrainCalls);
            Assert.Equal(ArtworkLifecycleFence.Uninstall, coordinator.LastDrainFence);

            // The pinned host removes only the versioned install folder, so the
            // plugin removes its own relocated state root once the drain is
            // complete, preserving the previous uninstall cleanup semantics.
            Assert.False(Directory.Exists(plugin.DataFolderPath));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void UninstallHookRetainsTheRelocatedStateRootWhenTheDrainIsIncomplete()
    {
        var root = CreateRoot();
        try
        {
            var paths = CreateApplicationPaths(root);
            var coordinator = new FakeArtworkLifecycleCoordinator(ArtworkLifecycleOutcome.Incomplete);
            using var provider = CreateProvider(coordinator);
            var plugin = new Plugin(paths, null!, provider);

            var sentinel = Path.Combine(plugin.DataFolderPath, "authoritative", "sentinel.json");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "{}");

            plugin.OnUninstalling();

            // An unresolved operation or restoration keeps its recovery records.
            Assert.True(Directory.Exists(plugin.DataFolderPath));
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static IApplicationPaths CreateApplicationPaths(string root)
    {
        var paths = DispatchProxy.Create<IApplicationPaths, TestApplicationPaths>();
        var stub = (TestApplicationPaths)(object)paths;
        stub.RootPath = root;
        return paths;
    }

    private static ServiceProvider CreateProvider(IArtworkLifecycleCoordinator coordinator)
    {
        return new ServiceCollection()
            .AddSingleton(coordinator)
            .BuildServiceProvider();
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "arrtags-state-location-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "plugins"));
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
    }

    private static bool IsUnder(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mirrors the pinned <c>PluginManager.LoadManifest</c> name derivation: a
    /// folder with no <c>meta.json</c> takes its base name with any
    /// <c>_&lt;version&gt;</c> suffix stripped.
    /// </summary>
    private static string DerivePluginName(string directory)
    {
        var baseName = Path.GetFileName(directory);
        var versionIndex = baseName.LastIndexOf('_');
        return versionIndex == -1 ? baseName : baseName[..versionIndex];
    }

    /// <summary>
    /// A minimal application-paths double that supplies only the members the
    /// plugin constructor reads.
    /// </summary>
    public class TestApplicationPaths : DispatchProxy
    {
        public string RootPath { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_ProgramDataPath" => RootPath,
                "get_PluginsPath" => Path.Combine(RootPath, "plugins"),
                "get_DataPath" => Path.Combine(RootPath, "data"),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }
    }

    private sealed class FakeArtworkLifecycleCoordinator : IArtworkLifecycleCoordinator
    {
        private readonly ArtworkLifecycleOutcome _outcome;

        public FakeArtworkLifecycleCoordinator(ArtworkLifecycleOutcome outcome)
        {
            _outcome = outcome;
        }

        public int DrainCalls { get; private set; }

        public ArtworkLifecycleFence LastDrainFence { get; private set; }

        public void ResetStaleFence()
        {
        }

        public Task<ArtworkLifecycleResult> DrainAsync(
            ArtworkLifecycleFence fence,
            CancellationToken cancellationToken)
        {
            DrainCalls++;
            LastDrainFence = fence;
            return Task.FromResult(ArtworkLifecycleResult.Create(fence, _outcome, "Drained."));
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
            return Task.FromResult(ArtworkRemovalResult.Create(
                ArtworkRemovalOutcome.NotConfirmed,
                "Not confirmed."));
        }
    }
}
