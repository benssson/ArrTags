using System;
using System.Diagnostics.CodeAnalysis;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The outcome of reading the current active source image for one V1 surface.
/// </summary>
public enum ArtworkSourceReadStatus
{
    /// <summary>The surface has a readable and confined source image.</summary>
    Present,

    /// <summary>The surface has no source image.</summary>
    Absent,

    /// <summary>The source image could not be used.</summary>
    Failed,
}

/// <summary>
/// The bounded reason a source read did not produce a present image. The values
/// are non-secret and carry no path, entity, or provider data.
/// </summary>
public enum ArtworkSourceReadFailureReason
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>The item was absent or the identifier was empty.</summary>
    ItemNotFound,

    /// <summary>The requested surface is not the V1 unindexed <c>Primary</c> surface.</summary>
    UnsupportedSurface,

    /// <summary>The image was empty, unreadable, corrupt, or could not be observed.</summary>
    Unreadable,

    /// <summary>The image container is not a confined PNG or JPEG.</summary>
    UnsupportedContentType,

    /// <summary>The image exceeds the configured source byte limit.</summary>
    SourceTooLarge,

    /// <summary>The image exceeds the configured decoded dimension limit.</summary>
    DimensionTooLarge,
}

/// <summary>
/// The immutable result of reading one active source image through the host
/// boundary. For a present image it carries the exact bounded bytes, the
/// confined content type, the post-orientation display dimensions, and the
/// Jellyfin identity fields; from one read the caller can construct both a valid
/// <see cref="SourceImageInput"/> and the task 5.2 <see cref="ActiveImageIdentity"/>.
/// It never contains a filesystem path, Jellyfin entity, provider DTO, network
/// handle, or credential.
/// </summary>
public sealed class ArtworkSourceReadResult
{
    private ArtworkSourceReadResult(
        ArtworkSourceReadStatus status,
        ArtworkSourceReadFailureReason failureReason,
        ArtworkImageSurface surface,
        string? contentType,
        ReadOnlyMemory<byte> bytes,
        string? contentSha256,
        int orientedWidth,
        int orientedHeight,
        DateTimeOffset? dateModifiedUtc,
        string? jellyfinImageTag,
        string reason)
    {
        Status = status;
        FailureReason = failureReason;
        Surface = surface;
        ContentType = contentType;
        Bytes = bytes;
        ContentSha256 = contentSha256;
        OrientedWidth = orientedWidth;
        OrientedHeight = orientedHeight;
        DateModifiedUtc = dateModifiedUtc;
        JellyfinImageTag = jellyfinImageTag;
        Reason = reason;
    }

    /// <summary>
    /// Gets the read status.
    /// </summary>
    public ArtworkSourceReadStatus Status { get; }

    /// <summary>
    /// Gets the failure classification when the status is <see cref="ArtworkSourceReadStatus.Failed"/>.
    /// </summary>
    public ArtworkSourceReadFailureReason FailureReason { get; }

    /// <summary>
    /// Gets the requested image surface.
    /// </summary>
    public ArtworkImageSurface Surface { get; }

    /// <summary>
    /// Gets the confined source content type when the status is <see cref="ArtworkSourceReadStatus.Present"/>.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// Gets the exact bounded source bytes when the status is <see cref="ArtworkSourceReadStatus.Present"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>
    /// Gets the exact source byte length when present; zero otherwise.
    /// </summary>
    public long ByteLength => Bytes.Length;

    /// <summary>
    /// Gets the uppercase SHA-256 of the exact source bytes when present.
    /// </summary>
    public string? ContentSha256 { get; }

    /// <summary>
    /// Gets the post-EXIF-orientation display width in pixels when present.
    /// </summary>
    public int OrientedWidth { get; }

    /// <summary>
    /// Gets the post-EXIF-orientation display height in pixels when present.
    /// </summary>
    public int OrientedHeight { get; }

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
    /// Creates a present result.
    /// </summary>
    /// <param name="surface">The image surface.</param>
    /// <param name="contentType">The confined content type.</param>
    /// <param name="bytes">The exact bounded source bytes.</param>
    /// <param name="contentSha256">The uppercase SHA-256 of the bytes.</param>
    /// <param name="orientedWidth">The post-orientation display width.</param>
    /// <param name="orientedHeight">The post-orientation display height.</param>
    /// <param name="dateModifiedUtc">The Jellyfin modification time when observable.</param>
    /// <param name="jellyfinImageTag">The Jellyfin image tag when available.</param>
    /// <returns>A present result.</returns>
    /// <exception cref="ArgumentException">The bytes, content type, or hash are invalid.</exception>
    public static ArtworkSourceReadResult Present(
        ArtworkImageSurface surface,
        string contentType,
        ReadOnlyMemory<byte> bytes,
        string contentSha256,
        int orientedWidth,
        int orientedHeight,
        DateTimeOffset? dateModifiedUtc,
        string? jellyfinImageTag)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!ArtworkSourceContentType.IsConfined(contentType))
        {
            throw new ArgumentException("A source read result accepts only a confined content type.", nameof(contentType));
        }

        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A present source read result requires at least one byte.", nameof(bytes));
        }

        if (!ArtworkHashes.IsSha256Hex(contentSha256))
        {
            throw new ArgumentException("A present source read result requires a SHA-256 content hash.", nameof(contentSha256));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orientedWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orientedHeight);

        return new ArtworkSourceReadResult(
            ArtworkSourceReadStatus.Present,
            ArtworkSourceReadFailureReason.None,
            surface,
            contentType,
            bytes,
            contentSha256,
            orientedWidth,
            orientedHeight,
            dateModifiedUtc,
            jellyfinImageTag,
            "The active source image was read.");
    }

    /// <summary>
    /// Creates an explicit absent result.
    /// </summary>
    /// <param name="surface">The image surface.</param>
    /// <returns>An absent result.</returns>
    public static ArtworkSourceReadResult Absent(ArtworkImageSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return new ArtworkSourceReadResult(
            ArtworkSourceReadStatus.Absent,
            ArtworkSourceReadFailureReason.None,
            surface,
            null,
            ReadOnlyMemory<byte>.Empty,
            null,
            0,
            0,
            null,
            null,
            "The image surface is absent.");
    }

    /// <summary>
    /// Creates a bounded failure result.
    /// </summary>
    /// <param name="surface">The image surface.</param>
    /// <param name="failureReason">The failure classification.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A failure result.</returns>
    public static ArtworkSourceReadResult Failed(
        ArtworkImageSurface surface,
        ArtworkSourceReadFailureReason failureReason,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 512)
        {
            throw new ArgumentException("A source read failure reason is bounded to 512 characters.", nameof(reason));
        }

        return new ArtworkSourceReadResult(
            ArtworkSourceReadStatus.Failed,
            failureReason,
            surface,
            null,
            ReadOnlyMemory<byte>.Empty,
            null,
            0,
            0,
            null,
            null,
            reason);
    }

    /// <summary>
    /// Attempts to construct the renderer input from a present result. The
    /// constructor re-verifies the declared hash against the exact bytes.
    /// </summary>
    /// <param name="input">The renderer input, or <see langword="null"/> when the result is not present.</param>
    /// <returns><see langword="true"/> when a renderer input was constructed.</returns>
    public bool TryCreateSourceImageInput([NotNullWhen(true)] out SourceImageInput? input)
    {
        if (Status != ArtworkSourceReadStatus.Present
            || ContentType is null
            || ContentSha256 is null)
        {
            input = null;
            return false;
        }

        input = new SourceImageInput(
            Bytes.Span,
            ContentType,
            OrientedWidth,
            OrientedHeight,
            ContentSha256);
        return true;
    }

    /// <summary>
    /// Attempts to construct the task 5.2 active-image identity from the read.
    /// A present result yields a present identity; an absent result yields an
    /// explicit absent identity; a failed result yields no identity.
    /// </summary>
    /// <param name="identity">The identity, or <see langword="null"/> when the read failed.</param>
    /// <returns><see langword="true"/> when an identity was constructed.</returns>
    public bool TryCreateActiveImageIdentity([NotNullWhen(true)] out ActiveImageIdentity? identity)
    {
        switch (Status)
        {
            case ArtworkSourceReadStatus.Present when ContentSha256 is not null:
                identity = new ActiveImageIdentity(
                    Surface,
                    ArtworkImagePresence.Present,
                    ContentSha256,
                    ByteLength,
                    OrientedWidth,
                    OrientedHeight,
                    DateModifiedUtc,
                    JellyfinImageTag);
                return true;
            case ArtworkSourceReadStatus.Absent:
                identity = ActiveImageIdentity.Absent(Surface);
                return true;
            default:
                identity = null;
                return false;
        }
    }
}
