using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace ArrTags.Artwork;

/// <summary>
/// The in-process per-item/image-surface serialization gate shared by normal
/// publication and recovery. Work for one subject is serialized through the same
/// gate so restart reconciliation and new publication can never interleave for
/// the same item and surface. The gate is intentionally process-local and
/// lazy; it performs no startup work and holds no durable state.
/// </summary>
internal static class ArtworkSubjectGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the serialization gate for one Jellyfin item and image surface.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface.</param>
    /// <returns>The subject gate.</returns>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    public static SemaphoreSlim Acquire(Guid jellyfinItemId, ArtworkImageSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var key = string.Concat(
            jellyfinItemId.ToString("N", CultureInfo.InvariantCulture),
            "-",
            surface.Key);
        return Gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
    }
}
