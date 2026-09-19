namespace ArrTags.Artwork;

/// <summary>
/// The external action a guarded state transition permits later Phase 5 tasks to
/// drive. The state machine never performs an image mutation itself; it only
/// records the decision so the publisher and lifecycle fencing can act.
/// </summary>
public enum ArtworkTransitionAction
{
    /// <summary>No image mutation is permitted; the image must be left unchanged.</summary>
    None,

    /// <summary>The caller may publish a verified derived image from the retained source.</summary>
    PublishDerived,

    /// <summary>The caller may restore the retained source artifact to the active surface.</summary>
    RestoreSource,

    /// <summary>The caller may remove the ArrTags image because the baseline was absent.</summary>
    RemoveActiveImage,
}
