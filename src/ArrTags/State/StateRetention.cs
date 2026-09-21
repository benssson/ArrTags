using System;
using System.Collections.Generic;
using System.IO;

namespace ArrTags.State;

/// <summary>
/// Applies bounded storage and retention policies. Cache records may be pruned
/// by age and quota. Authoritative records are never pruned as ordinary cache
/// entries; only records explicitly marked terminal are eligible for retention
/// cleanup.
/// </summary>
public static class StateRetention
{
    /// <summary>
    /// Prunes expired cache records and enforces the cache byte quota, evicting
    /// the oldest records first. Record kinds listed in
    /// <paramref name="exemptKinds"/> are governed by a different policy and are
    /// never touched here: metadata last-known-good usability is bounded by
    /// freshness, not by the render work-cache TTL or quota.
    /// </summary>
    /// <param name="paths">The safe state paths.</param>
    /// <param name="timeToLive">The cache record time-to-live.</param>
    /// <param name="quotaBytes">The maximum total cache bytes.</param>
    /// <param name="now">The current time.</param>
    /// <param name="exemptKinds">Record kinds excluded from render-cache retention.</param>
    /// <returns>The number of removed records.</returns>
    public static int ApplyCacheRetention(
        PluginStatePaths paths,
        TimeSpan timeToLive,
        long quotaBytes,
        DateTimeOffset now,
        IReadOnlyCollection<string>? exemptKinds = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var cacheDirectory = paths.GetDirectory(StateAuthority.Cache);
        if (!Directory.Exists(cacheDirectory))
        {
            return 0;
        }

        var removed = 0;
        var remaining = new List<FileInfo>();
        foreach (var file in new DirectoryInfo(cacheDirectory).EnumerateFiles("*.json", SearchOption.AllDirectories))
        {
            if (IsExempt(file, exemptKinds))
            {
                continue;
            }

            if (file.LastWriteTimeUtc + timeToLive < now.UtcDateTime)
            {
                if (TryDelete(file))
                {
                    removed++;
                }

                continue;
            }

            remaining.Add(file);
        }

        var totalBytes = 0L;
        foreach (var file in remaining)
        {
            totalBytes += file.Length;
        }

        if (totalBytes <= quotaBytes)
        {
            return removed;
        }

        remaining.Sort(static (left, right) => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));
        foreach (var file in remaining)
        {
            if (totalBytes <= quotaBytes)
            {
                break;
            }

            var length = file.Length;
            if (TryDelete(file))
            {
                totalBytes -= length;
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Prunes authoritative records that are explicitly terminal and past the
    /// retention window. Non-terminal or unreadable records are never removed.
    /// </summary>
    /// <param name="paths">The safe state paths.</param>
    /// <param name="retention">The terminal provenance retention window.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The number of removed records.</returns>
    public static int ApplyAuthoritativeRetention(PluginStatePaths paths, TimeSpan retention, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var directory = paths.GetDirectory(StateAuthority.Authoritative);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.json", SearchOption.AllDirectories))
        {
            if (!IsTerminalAndExpired(file, retention, now))
            {
                continue;
            }

            if (TryDelete(file))
            {
                removed++;
            }
        }

        return removed;
    }

    private static bool IsExempt(FileInfo file, IReadOnlyCollection<string>? exemptKinds)
    {
        if (exemptKinds is null || exemptKinds.Count == 0)
        {
            return false;
        }

        var kind = file.Directory?.Name;
        if (string.IsNullOrEmpty(kind))
        {
            return false;
        }

        foreach (var exempt in exemptKinds)
        {
            if (string.Equals(kind, exempt, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTerminalAndExpired(FileInfo file, TimeSpan retention, DateTimeOffset now)
    {
        try
        {
            var bytes = File.ReadAllBytes(file.FullName);
            if (!StateEnvelopeCodec.TryReadEnvelope(bytes, out var envelope, out _) || envelope is null)
            {
                return false;
            }

            return envelope.Terminal && envelope.UpdatedUtc + retention < now;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
