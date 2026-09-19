using System;

namespace ArrTags.Artwork;

/// <summary>
/// A persisted ownership comparison: the latest <see cref="ArtworkOwnershipStatus"/>
/// observed for one item/surface, the observation that produced it, and a bounded
/// non-secret explanation. It is retained for diagnostics and restart
/// revalidation and never substitutes for a fresh observation before mutation.
/// </summary>
public sealed class ArtworkOwnershipObservation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkOwnershipObservation"/> class.
    /// </summary>
    /// <param name="status">The comparison status.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="observed">The observed identity, or <see langword="null"/> when it could not be observed.</param>
    /// <param name="observedAt">The comparison time.</param>
    /// <exception cref="ArgumentOutOfRangeException">The status is undefined or the time is the default value.</exception>
    /// <exception cref="ArgumentException">The reason is missing or exceeds the bounded length.</exception>
    public ArtworkOwnershipObservation(
        ArtworkOwnershipStatus status,
        string reason,
        ActiveImageIdentity? observed,
        DateTimeOffset observedAt)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown ownership status.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 512)
        {
            throw new ArgumentException("An ownership observation reason is bounded to 512 characters.", nameof(reason));
        }

        if (observedAt == default(DateTimeOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(observedAt), observedAt, "An ownership observation time cannot be the default value.");
        }

        Status = status;
        Reason = reason;
        Observed = observed;
        ObservedAt = observedAt;
    }

    /// <summary>
    /// Gets the comparison status.
    /// </summary>
    public ArtworkOwnershipStatus Status { get; }

    /// <summary>
    /// Gets the bounded, non-secret explanation.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the observed identity, or <see langword="null"/> when the comparison could not be made.
    /// </summary>
    public ActiveImageIdentity? Observed { get; }

    /// <summary>
    /// Gets the comparison time.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }
}
