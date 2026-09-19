using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// A fact that verifies the real plugin archive produced by
/// <c>./build.sh package</c>. The default <c>./build.sh test</c> run does not
/// build the package, so the fact is reported as skipped when no
/// <c>artifacts/ArrTags_*.zip</c> archive is present; running
/// <c>./build.sh package</c> before the suite makes the package-content
/// assertions run.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PackagedPluginFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PackagedPluginFactAttribute"/> class.
    /// </summary>
    public PackagedPluginFactAttribute()
    {
        if (PluginPackagingPaths.TryFindPackageArchive() is null)
        {
            Skip = "Run ./build.sh package to produce artifacts/ArrTags_<version>.zip for package-content verification.";
        }
    }
}

/// <summary>
/// Locates the repository root and any plugin archive under <c>artifacts/</c>.
/// </summary>
internal static class PluginPackagingPaths
{
    /// <summary>
    /// Finds the repository root by walking up from the test output directory.
    /// </summary>
    /// <returns>The repository root, or <see langword="null"/> when it cannot be found.</returns>
    public static DirectoryInfo? TryFindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.yaml")))
        {
            directory = directory.Parent;
        }

        return directory;
    }

    /// <summary>
    /// Finds the newest produced plugin archive under the repository's
    /// <c>artifacts/</c> directory.
    /// </summary>
    /// <returns>The archive path, or <see langword="null"/> when none exists.</returns>
    public static string? TryFindPackageArchive()
    {
        var root = TryFindRepositoryRoot();
        if (root is null)
        {
            return null;
        }

        var artifacts = Path.Combine(root.FullName, "artifacts");
        if (!Directory.Exists(artifacts))
        {
            return null;
        }

        return Directory.EnumerateFiles(artifacts, "ArrTags_*.zip")
            .OrderBy(path => path, StringComparer.Ordinal)
            .LastOrDefault();
    }
}
