using System;

namespace ArrTags.Artwork;

/// <summary>
/// The result of one guarded logical transition: the resulting
/// <see cref="PublishedArtworkState"/>, the external action the state permits,
/// and a bounded, non-secret explanation. An action of
/// <see cref="ArtworkTransitionAction.None"/> always means the active image must
/// be left unchanged.
/// </summary>
public sealed class ArtworkTransitionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkTransitionResult"/> class.
    /// </summary>
    /// <param name="state">The resulting state.</param>
    /// <param name="action">The permitted external action.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The reason is missing or exceeds the bounded length.</exception>
    public ArtworkTransitionResult(PublishedArtworkState state, ArtworkTransitionAction action, string reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 512)
        {
            throw new ArgumentException("A transition reason is bounded to 512 characters.", nameof(reason));
        }

        State = state;
        Action = action;
        Reason = reason;
    }

    /// <summary>
    /// Gets the resulting artwork state.
    /// </summary>
    public PublishedArtworkState State { get; }

    /// <summary>
    /// Gets the external action the state permits.
    /// </summary>
    public ArtworkTransitionAction Action { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation of the decision.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets a value indicating whether an image mutation is permitted.
    /// </summary>
    public bool AllowsImageMutation => Action != ArtworkTransitionAction.None;
}
