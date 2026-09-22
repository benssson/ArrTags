#:property RestorePackagesWithLockFile=false
// ArrTags release packer: a deterministic (byte-reproducible) plugin archive
// builder.
//
// It is a .NET 10 file-based app (run with
// `dotnet run scripts/pack-release.cs -- --source <dir> --output <zip>`) and is
// NOT part of the plugin build or solution; it adds no plugin dependency.
//
// MSBuild's `ZipDirectory` task enumerates the staging directory in filesystem
// order and stamps every entry with the source file's modification time, so
// repeated `./build.sh package` runs produced the same extracted contents with
// different bytes and SHA-256 (the Phase 7 acceptance criterion 5 gap). This
// tool replaces that step for the release archive:
//
//   * entries are written in ordinal order by their forward-slash relative path;
//   * every entry is stamped with one fixed ZIP timestamp;
//   * the deflate stream is the pinned SDK's deterministic implementation.
//
// Given identical staged input bytes, the produced archive is byte-identical,
// so the release artifact has a stable SHA-256 identity.
//
// Usage:
//   dotnet run scripts/pack-release.cs -- --source <staging-dir> --output <zip-path>
//
// Exit codes: 0 success, 2 usage/input error.

using System.IO.Compression;

var source = default(string);
var output = default(string);

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--source" when i + 1 < args.Length:
            source = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            return 2;
    }
}

if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(output))
{
    Console.Error.WriteLine("Usage: dotnet run scripts/pack-release.cs -- --source <staging-dir> --output <zip-path>");
    return 2;
}

if (!Directory.Exists(source))
{
    Console.Error.WriteLine($"Staging directory not found: {source}");
    return 2;
}

// A single fixed timestamp for every entry. The value is interpreted as the
// ZIP entry's local wall-clock (the ZIP format has no timezone), so it is
// stable regardless of the build machine's timezone. It must be >= 1980.
var fixedTimestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

var files = Directory
    .EnumerateFiles(source, "*", SearchOption.AllDirectories)
    .Select(path => new
    {
        FullPath = path,
        EntryName = Path.GetRelativePath(source, path).Replace('\\', '/'),
    })
    .OrderBy(file => file.EntryName, StringComparer.Ordinal)
    .ToArray();

if (files.Length == 0)
{
    Console.Error.WriteLine($"Staging directory contains no files: {source}");
    return 2;
}

var outputFullPath = Path.GetFullPath(output);
var outputDirectory = Path.GetDirectoryName(outputFullPath);
if (!string.IsNullOrEmpty(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

if (File.Exists(outputFullPath))
{
    File.Delete(outputFullPath);
}

using (var fileStream = new FileStream(outputFullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
{
    foreach (var file in files)
    {
        var entry = archive.CreateEntry(file.EntryName, CompressionLevel.Optimal);
        entry.LastWriteTime = fixedTimestamp;

        using var entryStream = entry.Open();
        using var sourceStream = File.OpenRead(file.FullPath);
        sourceStream.CopyTo(entryStream);
    }
}

Console.WriteLine($"Created deterministic package: {outputFullPath} ({files.Length} entries)");

return 0;
