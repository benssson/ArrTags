namespace ArrTags.Artwork;

/// <summary>
/// The bounded outcome of one supported Jellyfin image mutation call. The values
/// are non-secret and carry no path, entity, or provider data, so a failure can
/// be recorded as a diagnostic without leaking host details.
/// </summary>
public enum ArtworkImageMutationStatus
{
    /// <summary>The supported mutation completed.</summary>
    Succeeded,

    /// <summary>The target item was absent or the identifier was empty.</summary>
    ItemNotFound,

    /// <summary>The requested surface is not the V1 unindexed <c>Primary</c> surface.</summary>
    UnsupportedSurface,

    /// <summary>The supplied bytes or content type are not a publishable image.</summary>
    InvalidContent,

    /// <summary>The supported mutation could not be completed.</summary>
    Failed,
}

/// <summary>
/// The immutable result of one image-save or item-update call through the
/// injectable <see cref="IArtworkImageWriter"/> boundary. A failure is a bounded
/// no-mutation result and never an exception into a Jellyfin operation, so the
/// publication orchestration can fail closed without disturbing the active
/// artwork.
/// </summary>
public sealed class ArtworkImageMutationResult
{
    private ArtworkImageMutationResult(ArtworkImageMutationStatus status, string reason)
    {
        Status = status;
        Reason = reason;
    }

    /// <summary>
    /// Gets the mutation status.
    /// </summary>
    public ArtworkImageMutationStatus Status { get; }

    /// <summary>
    /// Gets a value indicating whether the supported mutation completed.
    /// </summary>
    public bool Succeeded => Status == ArtworkImageMutationStatus.Succeeded;

    /// <summary>
    /// Gets a bounded, non-secret explanation.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static ArtworkImageMutationResult Success()
    {
        return new ArtworkImageMutationResult(ArtworkImageMutationStatus.Succeeded, "The image mutation completed.");
    }

    /// <summary>
    /// Creates a bounded failed result.
    /// </summary>
    /// <param name="status">The failure classification; must not be <see cref="ArtworkImageMutationStatus.Succeeded"/>.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A failed result.</returns>
    public static ArtworkImageMutationResult Failure(ArtworkImageMutationStatus status, string reason)
    {
        if (status == ArtworkImageMutationStatus.Succeeded)
        {
            status = ArtworkImageMutationStatus.Failed;
        }

        return new ArtworkImageMutationResult(status, ArtworkOperationErrors.Sanitize(reason) ?? "The image mutation failed.");
    }
}
