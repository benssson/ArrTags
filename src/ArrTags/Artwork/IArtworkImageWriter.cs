using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Artwork;

/// <summary>
/// The injectable, host-neutral boundary for the supported Jellyfin item-image
/// mutations used by publication. It exposes the two external effects of the
/// durable write-ahead protocol separately so the caller can persist the
/// <c>MutationStarted</c> and <c>RepositoryUpdateStarted</c> phases between them,
/// and it maps every failure to a bounded result instead of leaking a Jellyfin
/// exception. This is the only abstraction the publication orchestration uses
/// for image mutation; all Jellyfin references stay in the single implementation.
/// </summary>
/// <remarks>
/// V1 uses only the supported stream <c>SaveImage</c> overload with the durable
/// derived bytes. The filesystem-path overload is never used because it deletes
/// its source file, and the URL overload is never used for derived bytes.
/// </remarks>
public interface IArtworkImageWriter
{
    /// <summary>
    /// Publishes the supplied derivative image bytes to the V1 image surface
    /// through the supported stream <c>SaveImage</c> overload.
    /// </summary>
    /// <param name="itemId">A non-empty Jellyfin item identifier.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="content">The complete derivative image bytes.</param>
    /// <param name="contentType">The confined derivative content type.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded mutation result.</returns>
    Task<ArtworkImageMutationResult> SaveImageAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists the normal Jellyfin item update that accompanies an image
    /// mutation so the new image identity becomes the effective active image.
    /// </summary>
    /// <param name="itemId">A non-empty Jellyfin item identifier.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded mutation result.</returns>
    Task<ArtworkImageMutationResult> PersistItemUpdateAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the current image from the V1 surface through the supported
    /// Jellyfin item-image removal flow. This is used only to restore an absent
    /// source baseline; the implementation uses the supported deletion API and
    /// never deletes a media file or an image-cache entry directly.
    /// </summary>
    /// <param name="itemId">A non-empty Jellyfin item identifier.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded mutation result.</returns>
    Task<ArtworkImageMutationResult> RemoveImageAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken);
}
