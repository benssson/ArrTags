using System;

namespace ArrTags.Media;

/// <summary>
/// The canonical, immutable summary of one Jellyfin item's location and
/// media-source facts. It captures raw Jellyfin observations only; path mapping
/// and other matching policy are applied separately.
/// </summary>
public sealed class MediaLocationSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaLocationSummary"/> class.
    /// </summary>
    /// <param name="kind">The canonical location classification.</param>
    /// <param name="isFileProtocol">Whether any reported media source uses the local file protocol.</param>
    /// <param name="mediaSourceCount">The number of media sources reported for the item.</param>
    /// <param name="primaryPath">The raw, unmapped path of the primary media source when known.</param>
    /// <param name="isRemote">Whether the item or any reported media source is remote.</param>
    /// <param name="isStrm">Whether the item or any reported media source is a <c>.strm</c> reference.</param>
    /// <exception cref="ArgumentOutOfRangeException">The kind is undefined or the source count is negative.</exception>
    public MediaLocationSummary(
        MediaLocationKind kind,
        bool isFileProtocol,
        int mediaSourceCount,
        string? primaryPath = null,
        bool isRemote = false,
        bool isStrm = false)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown media location kind.");
        }

        if (mediaSourceCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mediaSourceCount),
                mediaSourceCount,
                "A media source count cannot be negative.");
        }

        Kind = kind;
        IsFileProtocol = isFileProtocol;
        MediaSourceCount = mediaSourceCount;
        PrimaryPath = primaryPath;
        IsRemote = isRemote;
        IsStrm = isStrm;
    }

    /// <summary>
    /// Gets the canonical location classification.
    /// </summary>
    public MediaLocationKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether any reported media source uses the local
    /// file protocol.
    /// </summary>
    public bool IsFileProtocol { get; }

    /// <summary>
    /// Gets the number of media sources reported for the item.
    /// </summary>
    public int MediaSourceCount { get; }

    /// <summary>
    /// Gets the raw, unmapped path of the primary media source when known.
    /// </summary>
    public string? PrimaryPath { get; }

    /// <summary>
    /// Gets a value indicating whether the item or any reported media source is
    /// remote. A remote source is never treated as a local badge file for V1.
    /// </summary>
    public bool IsRemote { get; }

    /// <summary>
    /// Gets a value indicating whether the item or any reported media source is a
    /// <c>.strm</c> reference. A <c>.strm</c> item is never treated as a local
    /// badge file for V1.
    /// </summary>
    public bool IsStrm { get; }
}
