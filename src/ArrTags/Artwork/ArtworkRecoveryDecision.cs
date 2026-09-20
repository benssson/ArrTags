using System;

namespace ArrTags.Artwork;

/// <summary>
/// The immutable outcome of evaluating the data-model 3.10.4 recovery decision
/// table for one <see cref="ArtworkOperation"/>. It carries the bounded action and
/// a bounded, non-secret reason and never contains a path, credential, provider
/// payload, or Jellyfin entity.
/// </summary>
public sealed class ArtworkRecoveryDecision
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkRecoveryDecision"/> class.
    /// </summary>
    /// <param name="action">The bounded recovery action.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <exception cref="ArgumentOutOfRangeException">The action is undefined.</exception>
    /// <exception cref="ArgumentException">The reason is missing or exceeds the bounded length.</exception>
    public ArtworkRecoveryDecision(ArtworkReconciliationAction action, string reason)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown recovery action.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 512)
        {
            throw new ArgumentException("A recovery decision reason is bounded to 512 characters.", nameof(reason));
        }

        Action = action;
        Reason = reason;
    }

    /// <summary>
    /// Gets the bounded recovery action.
    /// </summary>
    public ArtworkReconciliationAction Action { get; }

    /// <summary>
    /// Gets the bounded, non-secret explanation.
    /// </summary>
    public string Reason { get; }
}
