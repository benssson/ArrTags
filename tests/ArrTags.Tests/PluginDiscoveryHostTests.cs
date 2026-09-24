using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Higher-fidelity task 7.7 regression coverage that executes the pinned host's
/// real <c>PluginManager</c> discovery and cleanup code (from the host
/// <c>Emby.Server.Implementations.dll</c>) against a real plugins directory.
/// This proves the supported versioned install folder is deleted when a
/// same-named data folder exists inside the plugins path, and is preserved once
/// the plugin's state root is relocated outside it. The fact is skipped when
/// <c>ARRTAGS_JELLYFIN_HOST_DIR</c> is unset and fails when it is set but the
/// pinned assembly cannot be loaded.
/// </summary>
public class PluginDiscoveryHostTests
{
    [JellyfinHostFact]
    public void PinnedDiscoveryDeletesTheVersionedInstallFolderWhenStateLivesInsideThePluginsPath()
    {
        var root = CreateRoot();
        try
        {
            var pluginsPath = Path.Combine(root, "plugins");
            var installFolder = SeedVersionedInstall(pluginsPath);

            // The pre-fix, Jellyfin-derived data folder: PluginsPath/ArrTags.
            var collidingDataFolder = Path.Combine(pluginsPath, Plugin.StateFolderName);
            Directory.CreateDirectory(collidingDataFolder);
            File.WriteAllText(Path.Combine(collidingDataFolder, "sentinel.json"), "{}");

            var plugins = RunDiscovery(pluginsPath);

            Assert.False(Directory.Exists(installFolder), "The pinned host must delete the shadowed versioned install folder.");
            Assert.Empty(plugins);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [JellyfinHostFact]
    public void PinnedDiscoveryKeepsTheVersionedInstallFolderWhenStateIsRelocatedOutsideThePluginsPath()
    {
        var root = CreateRoot();
        try
        {
            var pluginsPath = Path.Combine(root, "plugins");
            var installFolder = SeedVersionedInstall(pluginsPath);

            // Derive the state root from the real production plugin so this fact
            // fails if the relocation is reverted (the data folder would then be
            // PluginsPath/ArrTags and the install folder would be deleted).
            var arrtagsPlugin = new Plugin(CreateApplicationPaths(root), null!, null!);
            var stateRoot = arrtagsPlugin.DataFolderPath;
            Assert.False(
                stateRoot.StartsWith(pluginsPath + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                $"The plugin state root '{stateRoot}' must not be inside the plugins path.");
            Directory.CreateDirectory(Path.Combine(stateRoot, "authoritative"));
            File.WriteAllText(Path.Combine(stateRoot, "authoritative", "sentinel.json"), "{}");

            var plugins = RunDiscovery(pluginsPath);

            Assert.True(Directory.Exists(installFolder), "The versioned install folder must survive discovery.");
            var plugin = Assert.Single(plugins);
            Assert.Equal("ArrTags", plugin.Name);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string SeedVersionedInstall(string pluginsPath)
    {
        var installFolder = Path.Combine(pluginsPath, "ArrTags_1.1.0.0");
        Directory.CreateDirectory(installFolder);

        // A valid plugin assembly is required for the host to keep the folder
        // (folders without DLLs are discarded after the version grouping pass).
        var builtAssembly = Path.Combine(AppContext.BaseDirectory, "ArrTags.dll");
        Assert.True(File.Exists(builtAssembly), $"Expected the built plugin assembly at {builtAssembly}.");
        File.Copy(builtAssembly, Path.Combine(installFolder, "ArrTags.dll"));

        File.WriteAllText(
            Path.Combine(installFolder, "meta.json"),
            """
            {
              "guid": "40322d52-5680-449f-b33e-e01836ee2f46",
              "name": "ArrTags",
              "targetAbi": "12.0.0.0",
              "version": "1.1.0.0",
              "status": "Active"
            }
            """);
        return installFolder;
    }

    private static IReadOnlyList<(string Name, string Path)> RunDiscovery(string pluginsPath)
    {
        var managerType = HostPluginManagerType.Value;
        var constructor = managerType.GetConstructors()
            .Single(candidate => candidate.GetParameters().Length == 5);

        var parameters = constructor.GetParameters();
        var arguments = parameters.Select(parameter => ArgumentFor(parameter.ParameterType)).ToArray();
        arguments[3] = pluginsPath;
        arguments[4] = new Version(12, 0, 0, 0);

        var manager = constructor.Invoke(arguments);
        var plugins = managerType.GetProperty("Plugins")!.GetValue(manager)!;

        var result = new List<(string Name, string Path)>();
        foreach (var plugin in (System.Collections.IEnumerable)plugins)
        {
            var type = plugin.GetType();
            result.Add((
                (string)type.GetProperty("Name")!.GetValue(plugin)!,
                (string)type.GetProperty("Path")!.GetValue(plugin)!));
        }

        return result;
    }

    private static object ArgumentFor(Type parameterType)
    {
        if (parameterType == typeof(string) || parameterType == typeof(Version))
        {
            // Replaced by the caller.
            return parameterType == typeof(string) ? string.Empty : new Version(0, 0, 0, 0);
        }

        if (parameterType == typeof(ServerConfiguration))
        {
            return new ServerConfiguration();
        }

        if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var implementation = typeof(NullLogger<>).MakeGenericType(parameterType.GetGenericArguments()[0]);
            return implementation.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        }

        if (parameterType.IsInterface)
        {
            return DispatchProxy.Create(parameterType, typeof(ThrowingDispatchProxy));
        }

        throw new InvalidOperationException($"Unexpected PluginManager constructor dependency '{parameterType.FullName}'.");
    }

    private static readonly Lazy<Type> HostPluginManagerType = new(() =>
    {
        var hostDirectory = Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR");
        Assert.False(string.IsNullOrWhiteSpace(hostDirectory), "ARRTAGS_JELLYFIN_HOST_DIR must be set for this fact.");

        var implementationsPath = Path.Combine(hostDirectory!, "Emby.Server.Implementations.dll");
        Assert.True(File.Exists(implementationsPath), $"Expected the pinned host Emby.Server.Implementations.dll at {implementationsPath}.");

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(hostDirectory!, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(implementationsPath);
        var managerType = assembly.GetType("Emby.Server.Implementations.Plugins.PluginManager", throwOnError: false);
        Assert.NotNull(managerType);
        return managerType!;
    });

    private static IApplicationPaths CreateApplicationPaths(string root)
    {
        var paths = DispatchProxy.Create<IApplicationPaths, PluginStateLocationTests.TestApplicationPaths>();
        ((PluginStateLocationTests.TestApplicationPaths)(object)paths).RootPath = root;
        return paths;
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "arrtags-discovery-host-" + Guid.NewGuid().ToString("N"));
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

    public class ThrowingDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException(targetMethod?.Name);
    }
}
