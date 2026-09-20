using System;

namespace ArrTags.Artwork;

/// <summary>
/// The durable, authoritative record of the active <see cref="ArtworkLifecycleFence"/>.
/// A non-normal fence prevents new publication work and is drained before the
/// plugin is disabled or uninstalled. The record contains only the fence, a
/// bounded non-secret reason, and timestamps; it never contains a credential, a
/// path, an entity, or a provider payload.
/// </summary>
public sealed class ArtworkLifecycleFenceRecord
{
    /// <summary>
    /// The current record model version. Changes to the fence semantics
    /// invalidate or migrate the record.
    /// </summary>
    public const int CurrentModelVersion = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkLifecycleFenceRecord"/> class.
    /// </summary>
    /// <param name="fence">The active lifecycle fence.</param>
    /// <param name="updatedAt">The fence change time.</param>
    /// <param name="modelVersion">The record model version; defaults to the current version.</param>
    /// <param name="reason">An optional bounded, non-secret explanation.</param>
    /// <exception cref="ArgumentOutOfRangeException">The fence is undefined.</exception>
    public ArtworkLifecycleFenceRecord(
        ArtworkLifecycleFence fence,
        DateTimeOffset updatedAt,
        int modelVersion = CurrentModelVersion,
        string? reason = null)
    {
        if (!Enum.IsDefined(fence))
        {
            throw new ArgumentOutOfRangeException(nameof(fence), fence, "Unknown artwork lifecycle fence.");
        }

        Fence = fence;
        UpdatedAt = updatedAt;
        ModelVersion = modelVersion;
        Reason = ArtworkOperationErrors.Sanitize(reason);
    }

    /// <summary>
    /// Gets the record model version.
    /// </summary>
    public int ModelVersion { get; }

    /// <summary>
    /// Gets the active lifecycle fence.
    /// </summary>
    public ArtworkLifecycleFence Fence { get; }

    /// <summary>
    /// Gets the fence change time.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Gets the bounded, non-secret explanation, or <see langword="null"/>.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Validates the documented invariants so a torn or invalid record is never
    /// treated as current state.
    /// </summary>
    /// <param name="reason">A bounded, non-secret failure explanation.</param>
    /// <returns><see langword="true"/> when the record is valid.</returns>
    public bool Validate(out string reason)
    {
        if (ModelVersion != CurrentModelVersion)
        {
            reason = "The lifecycle-fence model version is not supported.";
            return false;
        }

        if (!Enum.IsDefined(Fence))
        {
            reason = "The lifecycle fence is not a defined value.";
            return false;
        }

        if (UpdatedAt == default(DateTimeOffset))
        {
            reason = "A lifecycle-fence record requires an update time.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
