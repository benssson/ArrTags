using System;

namespace ArrTags.Artwork;

/// <summary>
/// The bounded outcome of handling one <c>ItemRemoved</c> library hint. A
/// removal is acted on only after the item absence is confirmed by a fresh read;
/// a confirmed removal tombstones the in-flight operation and performs no
/// Jellyfin image mutation.
/// </summary>
public enum ArtworkRemovalOutcome
{
    /// <summary>The item absence was not confirmed; nothing was changed.</summary>
    NotConfirmed,

    /// <summary>The item absence was confirmed and the plugin records were tombstoned.</summary>
    Confirmed,

    /// <summary>The confirmation read failed or the removal could not be recorded safely.</summary>
    Blocked,

    /// <summary>The handling was cancelled; nothing was changed.</summary>
    Cancelled,
}

/// <summary>
/// The immutable result of handling one item-removal hint. It carries only a
/// bounded reason and a count; it never contains a path, credential, entity, or
/// provider payload.
/// </summary>
public sealed class ArtworkRemovalResult
{
    private ArtworkRemovalResult(ArtworkRemovalOutcome outcome, string reason, bool tombstoned)
    {
        Outcome = outcome;
        Reason = reason;
        Tombstoned = tombstoned;
    }

    /// <summary>
    /// Gets the bounded removal outcome.
    /// </summary>
    public ArtworkRemovalOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether the plugin tombstone was written.
    /// </summary>
    public bool Tombstoned { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Creates a bounded item-removal result.
    /// </summary>
    /// <param name="outcome">The bounded outcome.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="tombstoned">Whether a tombstone was written.</param>
    /// <returns>An item-removal result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is undefined.</exception>
    public static ArtworkRemovalResult Create(
        ArtworkRemovalOutcome outcome,
        string reason,
        bool tombstoned = false)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown removal outcome.");
        }

        return new ArtworkRemovalResult(
            outcome,
            ArtworkOperationErrors.Sanitize(reason) ?? "The item removal was not confirmed.",
            tombstoned);
    }
}
