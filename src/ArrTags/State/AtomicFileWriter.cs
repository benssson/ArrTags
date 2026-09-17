using System;
using System.Globalization;
using System.IO;

namespace ArrTags.State;

/// <summary>
/// Writes files through a flushed temporary file and an atomic replacement in
/// the same directory, so a crash cannot leave a partially written record in
/// place of the previous value.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Writes bytes atomically to the supplied path.
    /// </summary>
    /// <param name="path">The destination path.</param>
    /// <param name="bytes">The bytes to write.</param>
    public static void Write(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Write(path, bytes.AsSpan());
    }

    /// <summary>
    /// Writes bytes atomically to the supplied path.
    /// </summary>
    /// <param name="path">The destination path.</param>
    /// <param name="bytes">The bytes to write.</param>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = string.Concat(
            path,
            ".tmp-",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
