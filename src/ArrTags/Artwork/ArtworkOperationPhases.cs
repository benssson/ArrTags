using System;

namespace ArrTags.Artwork;

/// <summary>
/// The pure, testable phase rules for a durable <see cref="ArtworkOperation"/>.
/// The normal protocol advances one documented step at a time, while recovery
/// may reach a terminal outcome from any non-terminal phase. The helper never
/// performs an image mutation and never persists anything; it only decides
/// whether a phase change is legal so the store and later recovery tasks can
/// reject stale or backward work deterministically.
/// </summary>
public static class ArtworkOperationPhases
{
    /// <summary>
    /// The documented forward order of the non-terminal and committed phases.
    /// <see cref="ArtworkOperationPhase.Aborted"/> and
    /// <see cref="ArtworkOperationPhase.RecoveryBlocked"/> are terminal outcomes
    /// reachable from any non-terminal phase and are not part of this order.
    /// </summary>
    private static readonly ArtworkOperationPhase[] NormalOrder =
    {
        ArtworkOperationPhase.Prepared,
        ArtworkOperationPhase.MutationStarted,
        ArtworkOperationPhase.RepositoryUpdateStarted,
        ArtworkOperationPhase.VerificationPending,
        ArtworkOperationPhase.FinalizationPending,
        ArtworkOperationPhase.Committed,
    };

    /// <summary>
    /// Determines whether the value is a defined phase.
    /// </summary>
    /// <param name="phase">The candidate phase.</param>
    /// <returns><see langword="true"/> when the phase is defined.</returns>
    public static bool IsDefined(ArtworkOperationPhase phase)
    {
        return Enum.IsDefined(phase);
    }

    /// <summary>
    /// Determines whether the phase is terminal. A terminal phase cannot advance;
    /// <see cref="ArtworkOperationPhase.RecoveryBlocked"/> is terminal until an
    /// explicit later reconciliation, and
    /// <see cref="ArtworkOperationPhase.Committed"/> and
    /// <see cref="ArtworkOperationPhase.Aborted"/> are final.
    /// </summary>
    /// <param name="phase">The phase.</param>
    /// <returns><see langword="true"/> when the phase is terminal.</returns>
    public static bool IsTerminal(ArtworkOperationPhase phase)
    {
        return phase is ArtworkOperationPhase.Committed
            or ArtworkOperationPhase.Aborted
            or ArtworkOperationPhase.RecoveryBlocked;
    }

    /// <summary>
    /// Determines whether the phase requires recovery to assume that an external
    /// image mutation may already have happened. This is the fail-safe
    /// lower-bound interpretation of the phase: only
    /// <see cref="ArtworkOperationPhase.Prepared"/> proves that no mutation was
    /// started. Every other phase, including the terminal
    /// <see cref="ArtworkOperationPhase.Aborted"/> and
    /// <see cref="ArtworkOperationPhase.RecoveryBlocked"/> outcomes that may be
    /// reached from any phase, must be treated as possibly having performed the
    /// external call.
    /// </summary>
    /// <param name="phase">The phase.</param>
    /// <returns><see langword="true"/> when a mutation may already have started.</returns>
    public static bool MayHaveStartedMutation(ArtworkOperationPhase phase)
    {
        return phase != ArtworkOperationPhase.Prepared;
    }

    /// <summary>
    /// Determines whether the phase may legally advance to another phase. A
    /// non-terminal phase may advance exactly one step along the documented
    /// order, or reach any terminal outcome (<see cref="ArtworkOperationPhase.Committed"/>,
    /// <see cref="ArtworkOperationPhase.Aborted"/>, or
    /// <see cref="ArtworkOperationPhase.RecoveryBlocked"/>) because recovery may
    /// resolve an interrupted operation from any phase. A terminal phase may
    /// never advance, and a phase may never move backward.
    /// </summary>
    /// <param name="from">The current durable phase.</param>
    /// <param name="to">The candidate phase.</param>
    /// <returns><see langword="true"/> when the transition is legal.</returns>
    public static bool CanAdvance(ArtworkOperationPhase from, ArtworkOperationPhase to)
    {
        if (!IsDefined(from) || !IsDefined(to))
        {
            return false;
        }

        if (IsTerminal(from) || from == to)
        {
            return false;
        }

        if (to is ArtworkOperationPhase.Committed
            or ArtworkOperationPhase.Aborted
            or ArtworkOperationPhase.RecoveryBlocked)
        {
            return true;
        }

        var fromIndex = IndexOf(from);
        var toIndex = IndexOf(to);
        return fromIndex >= 0 && toIndex == fromIndex + 1;
    }

    /// <summary>
    /// Attempts to advance to another phase and returns a bounded, non-secret
    /// explanation when the transition is refused.
    /// </summary>
    /// <param name="from">The current durable phase.</param>
    /// <param name="to">The candidate phase.</param>
    /// <param name="reason">A bounded explanation when the transition is refused.</param>
    /// <returns><see langword="true"/> when the transition is legal.</returns>
    public static bool TryAdvance(ArtworkOperationPhase from, ArtworkOperationPhase to, out string reason)
    {
        if (CanAdvance(from, to))
        {
            reason = string.Empty;
            return true;
        }

        if (!IsDefined(from) || !IsDefined(to))
        {
            reason = "The artwork operation phase is not defined.";
        }
        else if (IsTerminal(from))
        {
            reason = "The artwork operation phase is terminal and cannot advance.";
        }
        else if (from == to)
        {
            reason = "The artwork operation phase did not advance.";
        }
        else
        {
            reason = "The artwork operation phase transition is not permitted.";
        }

        return false;
    }

    private static int IndexOf(ArtworkOperationPhase phase)
    {
        for (var index = 0; index < NormalOrder.Length; index++)
        {
            if (NormalOrder[index] == phase)
            {
                return index;
            }
        }

        return -1;
    }
}
