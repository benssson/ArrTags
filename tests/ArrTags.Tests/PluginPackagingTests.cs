using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.4 packaging-contract tests, updated by task 7.8 (ADR-015). The
/// unguarded facts pin the shipped <c>build.yaml</c> artifact list and the
/// <c>PackagePlugin</c> MSBuild contract so a package that omits the dependency
/// manifest or reintroduces the duplicate SkiaSharp runtime cannot pass the
/// default suite. The <see cref="PackagedPluginFactAttribute"/> facts
/// additionally verify the real archive produced by <c>./build.sh package</c>
/// when it is present.
/// </summary>
public class PluginPackagingTests
{
    [Fact]
    public void BuildManifestKeepsPinnedIdentityAndAbi()
    {
        var manifest = ReadBuildManifest();

        Assert.Contains("name: \"ArrTags\"", manifest, StringComparison.Ordinal);
        Assert.Contains("guid: \"40322d52-5680-449f-b33e-e01836ee2f46\"", manifest, StringComparison.Ordinal);
        Assert.Contains("version: \"1.1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("targetAbi: \"12.0.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("framework: \"net10.0\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildManifestArtifactsListThePluginAssemblyAndManifest()
    {
        var artifacts = ReadBuildManifestArtifacts();

        Assert.Contains("ArrTags.dll", artifacts);
        Assert.Contains("ArrTags.deps.json", artifacts);
        Assert.DoesNotContain("SkiaSharp.dll", artifacts);
        Assert.DoesNotContain("libSkiaSharp.so", artifacts);
    }

    [Fact]
    public void PackageTargetDoesNotShipTheRendererRuntime()
    {
        var project = ReadPluginProject();

        Assert.Contains("Name=\"PackagePlugin\"", project, StringComparison.Ordinal);
        Assert.Contains("$(TargetName).deps.json", project, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", project, StringComparison.Ordinal);
        Assert.Contains("licenses", project, StringComparison.Ordinal);

        // ADR-015: the plugin compiles against the pinned SkiaSharp managed and
        // native packages but excludes their runtime assets, and it no longer
        // stages either renderer file.
        Assert.Contains("<PackageReference Include=\"SkiaSharp\" Version=\"3.119.4\">", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"SkiaSharp.NativeAssets.Linux\" Version=\"3.119.4\">", project, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(project, "<ExcludeAssets>runtime</ExcludeAssets>"));
        Assert.DoesNotContain("_ArrTagsManagedRendererAsset", project, StringComparison.Ordinal);
        Assert.DoesNotContain("_ArrTagsNativeRendererAsset", project, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeCopyLocalItems", project, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeTargetsCopyLocalItems", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedNoticeAndLicenseFilesExist()
    {
        var root = RequireRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root.FullName, "build.yaml")));
        Assert.True(File.Exists(Path.Combine(root.FullName, "THIRD-PARTY-NOTICES.md")));
        Assert.True(File.Exists(Path.Combine(root.FullName, "licenses", "DejaVu-Fonts-License.txt")));
        Assert.True(File.Exists(Path.Combine(root.FullName, "licenses", "SkiaSharp-LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(root.FullName, "licenses", "SkiaSharp-THIRD-PARTY-NOTICES.txt")));
    }

    [PackagedPluginFact]
    public void PackageContainsTheRequiredPluginRootFiles()
    {
        using var archive = ZipFile.OpenRead(RequirePackageArchive());

        foreach (var name in new[]
        {
            "ArrTags.dll",
            "ArrTags.deps.json",
            "build.yaml",
            "THIRD-PARTY-NOTICES.md",
            "licenses/DejaVu-Fonts-License.txt",
            "licenses/SkiaSharp-LICENSE.txt",
            "licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt",
        })
        {
            var entry = archive.GetEntry(name);
            Assert.NotNull(entry);
            Assert.True(entry!.Length > 0, $"Packaged entry {name} is empty.");
        }
    }

    [PackagedPluginFact]
    public void PackageHoldsEveryArtifactNamedInTheBuildManifest()
    {
        using var archive = ZipFile.OpenRead(RequirePackageArchive());

        foreach (var artifact in ReadBuildManifestArtifacts())
        {
            Assert.True(archive.GetEntry(artifact) is not null, $"build.yaml artifact {artifact} is missing from the package.");
        }
    }

    [PackagedPluginFact]
    public void PackageDoesNotContainTheDuplicateSkiaSharpRuntime()
    {
        using var archive = ZipFile.OpenRead(RequirePackageArchive());

        // ADR-015 regression: shipping either file recreates the task 7.3-F1
        // host/plugin SkiaSharp type-identity conflict.
        Assert.Null(archive.GetEntry("SkiaSharp.dll"));
        Assert.Null(archive.GetEntry("libSkiaSharp.so"));
    }

    private static string ReadBuildManifest()
    {
        return File.ReadAllText(Path.Combine(RequireRepositoryRoot().FullName, "build.yaml"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadPluginProject()
    {
        return File.ReadAllText(Path.Combine(RequireRepositoryRoot().FullName, "src", "ArrTags", "ArrTags.csproj"));
    }

    private static IReadOnlyList<string> ReadBuildManifestArtifacts()
    {
        var artifacts = new List<string>();
        var inBlock = false;

        foreach (var raw in ReadBuildManifest().Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.StartsWith("artifacts:", StringComparison.Ordinal))
            {
                inBlock = true;
                continue;
            }

            if (!inBlock)
            {
                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                artifacts.Add(trimmed[2..].Trim().Trim('"'));
            }
        }

        return artifacts;
    }

    private static DirectoryInfo RequireRepositoryRoot()
    {
        return PluginPackagingPaths.TryFindRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string RequirePackageArchive()
    {
        return PluginPackagingPaths.TryFindPackageArchive()
            ?? throw new InvalidOperationException("Could not locate a produced plugin archive.");
    }
}
