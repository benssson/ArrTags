using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.4 packaging-contract tests. The unguarded facts pin the
/// shipped <c>build.yaml</c> artifact list and the <c>PackagePlugin</c> MSBuild
/// contract so a package that omits the renderer's managed/native assets or the
/// dependency manifest cannot pass the default suite. The
/// <see cref="PackagedPluginFactAttribute"/> facts additionally verify the real
/// archive produced by <c>./build.sh package</c> when it is present.
/// </summary>
public class PluginPackagingTests
{
    private const string PinnedManagedSkiaSha256 = "aaaaa18c68ba1f3a3408b00dff28b11d5705198e17ba9d3aa59222bfd35407c8";
    private const string PinnedNativeSkiaSha256 = "66c856eaf1a47a00b23204c30c6ee407987bf5086ecc0a1a6b4fd67526b0cd02";

    [Fact]
    public void BuildManifestKeepsPinnedIdentityAndAbi()
    {
        var manifest = ReadBuildManifest();

        Assert.Contains("name: \"ArrTags\"", manifest, StringComparison.Ordinal);
        Assert.Contains("guid: \"40322d52-5680-449f-b33e-e01836ee2f46\"", manifest, StringComparison.Ordinal);
        Assert.Contains("version: \"0.1.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("targetAbi: \"12.0.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("framework: \"net10.0\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildManifestArtifactsListTheRendererRuntimeFiles()
    {
        var artifacts = ReadBuildManifestArtifacts();

        Assert.Contains("ArrTags.dll", artifacts);
        Assert.Contains("SkiaSharp.dll", artifacts);
        Assert.Contains("libSkiaSharp.so", artifacts);
        Assert.Contains("ArrTags.deps.json", artifacts);
    }

    [Fact]
    public void PackageTargetResolvesRendererAssetsFromPinnedReferences()
    {
        var project = ReadPluginProject();

        Assert.Contains("Name=\"PackagePlugin\"", project, StringComparison.Ordinal);
        Assert.Contains("RuntimeCopyLocalItems", project, StringComparison.Ordinal);
        Assert.Contains("RuntimeTargetsCopyLocalItems", project, StringComparison.Ordinal);
        Assert.Contains("libSkiaSharp.so", project, StringComparison.Ordinal);
        Assert.Contains("SkiaSharp.dll", project, StringComparison.Ordinal);
        Assert.Contains("$(TargetName).deps.json", project, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", project, StringComparison.Ordinal);
        Assert.Contains("licenses", project, StringComparison.Ordinal);
    }

    [Fact]
    public void V1ClaimsOnlyThePinnedLinuxX64Runtime()
    {
        var project = ReadPluginProject();

        Assert.Contains("<PluginRuntimeIdentifier Condition=\"'$(PluginRuntimeIdentifier)' == ''\">linux-x64</PluginRuntimeIdentifier>", project, StringComparison.Ordinal);
        Assert.Contains("'%(RuntimeTargetsCopyLocalItems.RuntimeIdentifier)' == '$(PluginRuntimeIdentifier)'", project, StringComparison.Ordinal);
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
            "SkiaSharp.dll",
            "libSkiaSharp.so",
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
    public void PackagedRendererAssetsAreThePinnedLinuxX64Files()
    {
        using var archive = ZipFile.OpenRead(RequirePackageArchive());

        var managed = ReadEntryBytes(archive, "SkiaSharp.dll");
        var native = ReadEntryBytes(archive, "libSkiaSharp.so");

        Assert.Equal(PinnedManagedSkiaSha256, Convert.ToHexString(SHA256.HashData(managed)).ToLowerInvariant());
        Assert.Equal(PinnedNativeSkiaSha256, Convert.ToHexString(SHA256.HashData(native)).ToLowerInvariant());

        // The V1 claim is linux-x64: a 64-bit little-endian ELF with e_machine EM_X86_64 (0x3e).
        Assert.Equal(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }, native[..4]);
        Assert.Equal(2, native[4]);
        Assert.Equal(1, native[5]);
        Assert.Equal(0x3e, (int)BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(18, 2)));
    }

    private static string ReadBuildManifest()
    {
        return File.ReadAllText(Path.Combine(RequireRepositoryRoot().FullName, "build.yaml"));
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

    private static byte[] ReadEntryBytes(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);

        using var stream = entry!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
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
