using System;
using System.Globalization;

namespace ArrTags.Artwork;

/// <summary>
/// The canonical identity of one Jellyfin image surface: an
/// <see cref="ArtworkImageType"/> plus the optional image index. The surface is
/// part of every active-image identity so an image recorded on another surface
/// or index can never satisfy an ownership comparison. V1 uses the unindexed
/// <see cref="Primary"/> surface only; a Jellyfin single-image index of zero is
/// represented as <see langword="null"/> because it denotes the unindexed route.
/// </summary>
public sealed class ArtworkImageSurface : IEquatable<ArtworkImageSurface>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkImageSurface"/> class.
    /// </summary>
    /// <param name="imageType">The image type.</param>
    /// <param name="index">The optional non-negative image index; <see langword="null"/> is the unindexed surface.</param>
    /// <exception cref="ArgumentOutOfRangeException">The image type is undefined or the index is negative.</exception>
    public ArtworkImageSurface(ArtworkImageType imageType, int? index = null)
    {
        if (!Enum.IsDefined(imageType))
        {
            throw new ArgumentOutOfRangeException(nameof(imageType), imageType, "Unknown artwork image type.");
        }

        if (index is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "An image surface index cannot be negative.");
        }

        ImageType = imageType;
        Index = index;
    }

    /// <summary>
    /// Gets the unindexed <c>Primary</c> poster surface used by V1.
    /// </summary>
    public static ArtworkImageSurface Primary { get; } = new(ArtworkImageType.Primary);

    /// <summary>
    /// Gets the image type.
    /// </summary>
    public ArtworkImageType ImageType { get; }

    /// <summary>
    /// Gets the optional image index, or <see langword="null"/> for the unindexed surface.
    /// </summary>
    public int? Index { get; }

    /// <summary>
    /// Gets a stable, path-safe key for the surface. It never contains path
    /// separators, so it may be used in a state record identifier.
    /// </summary>
    public string Key => Index is null
        ? ImageType.ToString()
        : string.Create(CultureInfo.InvariantCulture, $"{ImageType}-i{Index.Value}");

    /// <inheritdoc />
    public bool Equals(ArtworkImageSurface? other)
    {
        return other is not null && ImageType == other.ImageType && Index == other.Index;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as ArtworkImageSurface);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ImageType, Index);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Key;
    }
}
