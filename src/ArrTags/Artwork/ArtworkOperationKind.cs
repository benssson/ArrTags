namespace ArrTags.Artwork;

/// <summary>
/// The kind of durable <see cref="ArtworkOperation"/>. The kind selects the
/// target final state and which artifact references the operation carries: a
/// publication produces a derived image, while a restoration returns the surface
/// to the retained source baseline.
/// </summary>
public enum ArtworkOperationKind
{
    /// <summary>Publishes a derived image as the active surface.</summary>
    Publication,

    /// <summary>Restores the retained source baseline, or removes an ArrTags image when the baseline was absent.</summary>
    Restoration,
}
