using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Artwork;

/// <summary>
/// The plugin-owned, host-neutral boundary that reads the current active source
/// image for one V1 surface. It returns a bounded result that carries the exact
/// source bytes, the confined content type, the post-orientation display
/// dimensions, and the Jellyfin identity fields needed by the renderer and the
/// artwork provenance state. A failure never escapes as an exception into a
/// Jellyfin operation.
/// </summary>
public interface IArtworkSourceReader
{
    /// <summary>
    /// Reads the current active source image for the requested item and surface.
    /// </summary>
    /// <param name="itemId">A non-empty Jellyfin item identifier.</param>
    /// <param name="surface">The requested image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded source read result.</returns>
    Task<ArtworkSourceReadResult> ReadAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken);
}
