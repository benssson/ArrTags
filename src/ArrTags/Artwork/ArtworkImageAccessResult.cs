using System;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The outcome of observing the current representation of one Jellyfin image
/// surface through the injectable image-access boundary. Absence is an explicit
/// identity value; a failure is a bounded no-source result and never an
/// exception into a Jellyfin operation.
/// </summary>
public enum ArtworkImageAccessStatus
{
    /// <summary>The surface has a readable active image.</summary>
    Present,

    /// <summary>The surface has no image.</summary>
    Absent,

    /// <summary>The surface could not be observed.</summary>
    Failed,
}

/// <summary>
/// The bounded reason an image-access observation did not produce a present
/// representation. The values are non-secret and carry no path or entity.
/// </summary>
public enum ArtworkImageAccessFailure
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>The item was absent or the identifier was empty.</summary>
    ItemNotFound,

    /// <summary>The requested surface is not the V1 unindexed <c>Primary</c> surface.</summary>
    UnsupportedSurface,

    /// <summary>The observed bytes are not a confined PNG or JPEG container.</summary>
    UnsupportedContentType,

    /// <summary>The representation exceeds the configured source byte limit.</summary>
    SourceTooLarge,

    /// <summary>The representation is empty, unreadable, corrupt, or otherwise unavailable.</summary>
    Unreadable,
}

/// <summary>
/// The immutable, host-neutral observation of one Jellyfin image surface. It
/// carries the exact bounded representation bytes, the host-observed JPEG/PNG
/// header dimensions and EXIF orientation, and the Jellyfin identity fields
/// needed to build a <see cref="SourceImageInput"/> and an
/// <see cref="ActiveImageIdentity"/>. It never contains a filesystem path,
/// Jellyfin entity, provider DTO, or mutable image object.
/// </summary>
public sealed class ArtworkImageAccessResult
{
    private ArtworkImageAccessResult(
        ArtworkImageAccessStatus status,
        ArtworkImageAccessFailure failure,
        ReadOnlyMemory<byte> bytes,
        int encodedWidth,
        int encodedHeight,
        SourceOrientation orientation,
        DateTimeOffset? dateModifiedUtc,
        string? jellyfinImageTag,
        string reason)
    {
        Status = status;
        Failure = failure;
        Bytes = bytes;
        EncodedWidth = encodedWidth;
        EncodedHeight = encodedHeight;
        Orientation = orientation;
        DateModifiedUtc = dateModifiedUtc;
        JellyfinImageTag = jellyfinImageTag;
        Reason = reason;
    }

    /// <summary>
    /// Gets the observation status.
    /// </summary>
    public ArtworkImageAccessStatus Status { get; }

    /// <summary>
    /// Gets the failure classification when the status is <see cref="ArtworkImageAccessStatus.Failed"/>.
    /// </summary>
    public ArtworkImageAccessFailure Failure { get; }

    /// <summary>
    /// Gets the exact bounded representation bytes when the status is <see cref="ArtworkImageAccessStatus.Present"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>
    /// Gets the pre-orientation encoded width in pixels when present.
    /// </summary>
    public int EncodedWidth { get; }

    /// <summary>
    /// Gets the pre-orientation encoded height in pixels when present.
    /// </summary>
    public int EncodedHeight { get; }

    /// <summary>
    /// Gets the provider-neutral EXIF orientation of the representation.
    /// </summary>
    public SourceOrientation Orientation { get; }

    /// <summary>
    /// Gets the Jellyfin image modification time when observable.
    /// </summary>
    public DateTimeOffset? DateModifiedUtc { get; }

    /// <summary>
    /// Gets the Jellyfin image tag when available. It is supporting replacement
    /// evidence and is never sufficient to prove ArrTags ownership.
    /// </summary>
    public string? JellyfinImageTag { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Creates a present observation.
    /// </summary>
    /// <param name="bytes">The exact bounded representation bytes.</param>
    /// <param name="encodedWidth">The encoded width in pixels.</param>
    /// <param name="encodedHeight">The encoded height in pixels.</param>
    /// <param name="orientation">The EXIF orientation.</param>
    /// <param name="dateModifiedUtc">The Jellyfin modification time when observable.</param>
    /// <param name="jellyfinImageTag">The Jellyfin image tag when available.</param>
    /// <returns>A present observation.</returns>
    /// <exception cref="ArgumentException">The bytes are empty or a dimension is not positive.</exception>
    public static ArtworkImageAccessResult Present(
        ReadOnlyMemory<byte> bytes,
        int encodedWidth,
        int encodedHeight,
        SourceOrientation orientation,
        DateTimeOffset? dateModifiedUtc,
        string? jellyfinImageTag)
    {
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A present image observation requires at least one byte.", nameof(bytes));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(encodedWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(encodedHeight);

        return new ArtworkImageAccessResult(
            ArtworkImageAccessStatus.Present,
            ArtworkImageAccessFailure.None,
            bytes,
            encodedWidth,
            encodedHeight,
            orientation,
            dateModifiedUtc,
            jellyfinImageTag,
            "The active image representation was read.");
    }

    /// <summary>
    /// Creates an absent observation.
    /// </summary>
    /// <returns>An absent observation.</returns>
    public static ArtworkImageAccessResult Absent()
    {
        return new ArtworkImageAccessResult(
            ArtworkImageAccessStatus.Absent,
            ArtworkImageAccessFailure.None,
            ReadOnlyMemory<byte>.Empty,
            0,
            0,
            SourceOrientation.None,
            null,
            null,
            "The image surface is absent.");
    }

    /// <summary>
    /// Creates a failed observation.
    /// </summary>
    /// <param name="failure">The failure classification.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A failed observation.</returns>
    public static ArtworkImageAccessResult Failed(ArtworkImageAccessFailure failure, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ArtworkImageAccessResult(
            ArtworkImageAccessStatus.Failed,
            failure,
            ReadOnlyMemory<byte>.Empty,
            0,
            0,
            SourceOrientation.None,
            null,
            null,
            reason);
    }
}
