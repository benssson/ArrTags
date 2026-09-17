using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArrTags.Configuration;
using MediaBrowser.Common.Plugins;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Foundation-level checks for the plugin identity, manifest ABI, and pinned
/// Jellyfin dependency graph. These tests require no live Jellyfin host.
/// </summary>
public class PluginFoundationTests
{
    [Fact]
    public void ManifestDeclaresPinnedAbiAndFramework()
    {
        var manifest = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "build.yaml"));

        Assert.Contains("targetAbi: \"12.0.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("framework: \"net10.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("ArrTags.dll", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginIdentityMatchesManifestGuid()
    {
        var manifest = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "build.yaml"));
        var manifestGuid = Regex.Match(manifest, "guid:\\s*\"([^\"]+)\"").Groups[1].Value;

        var plugin = (IPlugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));

        Assert.Equal("ArrTags", plugin.Name);
        Assert.Equal(manifestGuid, plugin.Id.ToString());
    }

    [Fact]
    public void PluginDerivesFromJellyfinPluginBase()
    {
        Assert.True(typeof(BasePlugin<PluginConfiguration>).IsAssignableFrom(typeof(Plugin)));
    }

    [Fact]
    public void PluginConfigurationIsConstructible()
    {
        Assert.NotNull(new PluginConfiguration());
    }

    [Fact]
    public void ResolvedJellyfinPackagesArePinnedTo12_0_0()
    {
        var assetsPath = Path.Combine(
            FindRepositoryRoot().FullName,
            "src",
            "ArrTags",
            "obj",
            "project.assets.json");

        Assert.True(File.Exists(assetsPath), $"Expected restored assets at {assetsPath}.");

        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var libraries = document.RootElement.GetProperty("libraries");

        foreach (var library in libraries.EnumerateObject())
        {
            var name = library.Name;
            var separator = name.IndexOf('/', StringComparison.Ordinal);
            var packageName = separator >= 0 ? name[..separator] : name;
            var version = separator >= 0 ? name[(separator + 1)..] : string.Empty;

            if (packageName.StartsWith("Jellyfin.", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("12.0.0", version);
            }
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.yaml")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
