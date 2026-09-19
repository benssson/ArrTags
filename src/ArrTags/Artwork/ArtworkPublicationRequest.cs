using System;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The single-subject input to the publication orchestration: one Jellyfin item,
/// one V1 image surface, and one completed render result. The request carries no
/// source bytes, source artifact, ownership token, or operation identity; the
/// publisher establishes those from the authoritative artwork state and the host
/// source adapter so a caller can never inject a derived image as a new source.
/// </summary>
public sealed class ArtworkPublicationRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkPublicationRequest"/> class.
    /// </summary>
    /// <param name="jellyfinItemId">The target Jellyfin item identifier.</param>
    /// <param name="imageSurface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="rendered">The completed render result carrying the derivative PNG bytes.</param>
    /// <exception cref="ArgumentNullException">The surface or render result is <see langword="null"/>.</exception>
    public ArtworkPublicationRequest(Guid jellyfinItemId, ArtworkImageSurface imageSurface, RenderResult rendered)
    {
        ArgumentNullException.ThrowIfNull(imageSurface);
        ArgumentNullException.ThrowIfNull(rendered);

        JellyfinItemId = jellyfinItemId;
        ImageSurface = imageSurface;
        Rendered = rendered;
    }

    /// <summary>
    /// Gets the target Jellyfin item identifier.
    /// </summary>
    public Guid JellyfinItemId { get; }

    /// <summary>
    /// Gets the image surface.
    /// </summary>
    public ArtworkImageSurface ImageSurface { get; }

    /// <summary>
    /// Gets the completed render result.
    /// </summary>
    public RenderResult Rendered { get; }
}
