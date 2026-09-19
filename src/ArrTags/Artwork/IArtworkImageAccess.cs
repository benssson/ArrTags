using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Artwork;

/// <summary>
/// The injectable seam between the host-neutral source reader and the concrete
/// Jellyfin image store. It observes the current representation of one image
/// surface and returns bounded bytes plus the Jellyfin identity fields, so the
/// reader's confinement, hashing, dimension, and limit logic can be tested
/// without a live Jellyfin host. It never exposes a Jellyfin entity or path to
/// the renderer.
/// </summary>
public interface IArtworkImageAccess
{
    /// <summary>
    /// Observes the current active representation of one image surface.
    /// </summary>
    /// <param name="itemId">A non-empty Jellyfin item identifier.</param>
    /// <param name="surface">The requested image surface.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded observation result.</returns>
    Task<ArtworkImageAccessResult> AccessAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken);
}
