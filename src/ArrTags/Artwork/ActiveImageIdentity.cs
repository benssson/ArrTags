using System;

namespace ArrTags.Artwork;

/// <summary>
/// The observable identity of one Jellyfin image surface at one moment. It is
/// the only evidence ArrTags uses to prove that a plugin-published image is
/// still active: the content hash is required for a present surface, and every
/// recorded Jellyfin value must still match when it can be observed. A path is
/// deliberately absent because Jellyfin may reuse a path for a different image,
/// and the Jellyfin image tag is supporting replacement evidence only, never an
/// ArrTags ownership token.
/// </summary>
/// <remarks>
/// A present identity with a missing content hash is a legal observation; it is
/// simply unusable as ownership proof and therefore always compares as unknown.
/// The stricter requirement that a persisted active identity carry a hash is
/// enforced by <see cref="PublishedArtworkState.Validate"/>.
/// </remarks>
public sealed class ActiveImageIdentity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveImageIdentity"/> class.
    /// </summary>
    /// <param name="surface">The image surface; required.</param>
    /// <param name="presence">Whether the surface is present or absent.</param>
    /// <param name="contentSha256">The SHA-256 of the bounded active representation for a present surface.</param>
    /// <param name="byteLength">The active representation byte length when known.</param>
    /// <param name="width">The active image width in pixels when known.</param>
    /// <param name="height">The active image height in pixels when known.</param>
    /// <param name="dateModifiedUtc">The Jellyfin image modification timestamp when known.</param>
    /// <param name="jellyfinImageTag">The Jellyfin image tag when available; not ownership proof.</param>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The presence is undefined, a dimension is not positive, or the modification time is the default value.</exception>
    /// <exception cref="ArgumentException">A hash is not a hexadecimal SHA-256 or an absent identity carries present-only values.</exception>
    public ActiveImageIdentity(
        ArtworkImageSurface surface,
        ArtworkImagePresence presence,
        string? contentSha256 = null,
        long? byteLength = null,
        int? width = null,
        int? height = null,
        DateTimeOffset? dateModifiedUtc = null,
        string? jellyfinImageTag = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!Enum.IsDefined(presence))
        {
            throw new ArgumentOutOfRangeException(nameof(presence), presence, "Unknown image presence.");
        }

        if (contentSha256 is not null && !ArtworkHashes.IsSha256Hex(contentSha256))
        {
            throw new ArgumentException("An image identity content hash must be a 64-character SHA-256 hex value.", nameof(contentSha256));
        }

        if (byteLength is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "An image identity byte length cannot be negative.");
        }

        if (width is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "An image identity width must be positive.");
        }

        if (height is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "An image identity height must be positive.");
        }

        if (dateModifiedUtc == default(DateTimeOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(dateModifiedUtc), dateModifiedUtc, "An image identity modification time cannot be the default value.");
        }

        if (jellyfinImageTag is { Length: > 512 })
        {
            throw new ArgumentException("An image identity tag is bounded to 512 characters.", nameof(jellyfinImageTag));
        }

        if (presence == ArtworkImagePresence.Absent)
        {
            if (contentSha256 is not null
                || byteLength is not null
                || width is not null
                || height is not null
                || dateModifiedUtc is not null
                || jellyfinImageTag is not null)
            {
                throw new ArgumentException("An absent image identity cannot carry present-only values.", nameof(presence));
            }
        }

        Surface = surface;
        Presence = presence;
        ContentSha256 = contentSha256;
        ByteLength = byteLength;
        Width = width;
        Height = height;
        DateModifiedUtc = dateModifiedUtc;
        JellyfinImageTag = jellyfinImageTag;
    }

    /// <summary>
    /// Gets the image surface.
    /// </summary>
    public ArtworkImageSurface Surface { get; }

    /// <summary>
    /// Gets whether the surface is present or absent.
    /// </summary>
    public ArtworkImagePresence Presence { get; }

    /// <summary>
    /// Gets the SHA-256 of the bounded active representation, or <see langword="null"/> when it could not be observed.
    /// </summary>
    public string? ContentSha256 { get; }

    /// <summary>
    /// Gets the active representation byte length when known.
    /// </summary>
    public long? ByteLength { get; }

    /// <summary>
    /// Gets the active image width in pixels when known.
    /// </summary>
    public int? Width { get; }

    /// <summary>
    /// Gets the active image height in pixels when known.
    /// </summary>
    public int? Height { get; }

    /// <summary>
    /// Gets the Jellyfin image modification timestamp when known.
    /// </summary>
    public DateTimeOffset? DateModifiedUtc { get; }

    /// <summary>
    /// Gets the Jellyfin image tag when available. It is supporting replacement
    /// evidence and is never sufficient to prove ArrTags ownership.
    /// </summary>
    public string? JellyfinImageTag { get; }

    /// <summary>
    /// Gets a value indicating whether this present identity carries the content
    /// hash required for an ownership proof.
    /// </summary>
    public bool HasContentHash => Presence == ArtworkImagePresence.Present && ContentSha256 is not null;

    /// <summary>
    /// Creates an explicit absent-surface identity.
    /// </summary>
    /// <param name="surface">The image surface.</param>
    /// <returns>An absent identity.</returns>
    public static ActiveImageIdentity Absent(ArtworkImageSurface surface)
    {
        return new ActiveImageIdentity(surface, ArtworkImagePresence.Absent);
    }
}
