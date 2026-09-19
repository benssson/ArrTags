namespace ArrTags.Rendering;

/// <summary>
/// The provider-neutral orientation of a decoded source image. The values match
/// the eight EXIF orientation cases. Orientation is resolved before layout so
/// every dimension and coordinate is expressed in output pixels.
/// </summary>
public enum SourceOrientation
{
    /// <summary>An unspecified orientation, treated as no transform.</summary>
    None = 0,

    /// <summary>Row zero is the visual top; no transform is required.</summary>
    TopLeft = 1,

    /// <summary>The image is mirrored horizontally.</summary>
    TopRight = 2,

    /// <summary>The image is rotated 180 degrees.</summary>
    BottomRight = 3,

    /// <summary>The image is mirrored vertically.</summary>
    BottomLeft = 4,

    /// <summary>The image is transposed across the main diagonal.</summary>
    LeftTop = 5,

    /// <summary>The image is rotated 90 degrees clockwise.</summary>
    RightTop = 6,

    /// <summary>The image is transposed across the anti-diagonal.</summary>
    RightBottom = 7,

    /// <summary>The image is rotated 270 degrees clockwise.</summary>
    LeftBottom = 8,
}
