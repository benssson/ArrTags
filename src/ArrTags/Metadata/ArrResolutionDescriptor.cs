namespace ArrTags.Metadata;

/// <summary>
/// The canonical resolution descriptor. Inspected media dimensions are preferred
/// over a quality classification, and the origin records which source was used.
/// </summary>
public sealed class ArrResolutionDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrResolutionDescriptor"/> class.
    /// </summary>
    /// <param name="width">The reported width in pixels when known.</param>
    /// <param name="height">The reported height in pixels when known.</param>
    /// <param name="label">The reported resolution label when known.</param>
    /// <param name="origin">The source of the reported values.</param>
    public ArrResolutionDescriptor(int? width, int? height, string? label, ArrMetadataOrigin origin)
    {
        Width = width;
        Height = height;
        Label = label;
        Origin = origin;
    }

    /// <summary>
    /// Gets the reported width in pixels when known.
    /// </summary>
    public int? Width { get; }

    /// <summary>
    /// Gets the reported height in pixels when known.
    /// </summary>
    public int? Height { get; }

    /// <summary>
    /// Gets the reported resolution label when known.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Gets the source of the reported values.
    /// </summary>
    public ArrMetadataOrigin Origin { get; }
}
