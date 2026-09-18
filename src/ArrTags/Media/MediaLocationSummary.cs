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
    /// <exception cref="ArgumentOutOfRangeException">The kind is undefined or the source count is negative.</exception>
    public MediaLocationSummary(
        MediaLocationKind kind,
        bool isFileProtocol,
        int mediaSourceCount,
        string? primaryPath = null)
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
}
