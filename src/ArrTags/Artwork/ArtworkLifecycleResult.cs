using System;

namespace ArrTags.Artwork;

/// <summary>
/// The bounded outcome of one disable or uninstall lifecycle drain. An
/// <see cref="Incomplete"/> outcome means at least one operation or restoration
/// could not be resolved safely; the active image and its recovery records are
/// left in place and no completion is claimed. The result never contains a path,
/// credential, entity, or provider payload.
/// </summary>
public enum ArtworkLifecycleOutcome
{
    /// <summary>No disable or uninstall fence was active; nothing was changed.</summary>
    NothingToDo,

    /// <summary>Every operation was reconciled and every owned image was restored.</summary>
    Completed,

    /// <summary>At least one operation or restoration remains unresolved and its records are retained.</summary>
    Incomplete,

    /// <summary>The drain was cancelled; the active artwork and recovery records are retained.</summary>
    Cancelled,
}

/// <summary>
/// The immutable result of one lifecycle drain. It carries only bounded counts
/// and a bounded non-secret reason.
/// </summary>
public sealed class ArtworkLifecycleResult
{
    private ArtworkLifecycleResult(
        ArtworkLifecycleFence fence,
        ArtworkLifecycleOutcome outcome,
        string reason,
        int operationsReconciled,
        int restorationsCompleted,
        int restorationsBlocked)
    {
        Fence = fence;
        Outcome = outcome;
        Reason = reason;
        OperationsReconciled = operationsReconciled;
        RestorationsCompleted = restorationsCompleted;
        RestorationsBlocked = restorationsBlocked;
    }

    /// <summary>
    /// Gets the lifecycle fence that was drained.
    /// </summary>
    public ArtworkLifecycleFence Fence { get; }

    /// <summary>
    /// Gets the bounded drain outcome.
    /// </summary>
    public ArtworkLifecycleOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether the drain completed without leaving an
    /// unresolved restoration.
    /// </summary>
    public bool IsComplete => Outcome is ArtworkLifecycleOutcome.Completed
        or ArtworkLifecycleOutcome.NothingToDo;

    /// <summary>
    /// Gets a bounded, non-secret explanation.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the number of non-terminal operations reconciled to a terminal result.
    /// </summary>
    public int OperationsReconciled { get; }

    /// <summary>
    /// Gets the number of owned surfaces whose retained baseline was restored.
    /// </summary>
    public int RestorationsCompleted { get; }

    /// <summary>
    /// Gets the number of owned surfaces whose restoration was blocked, changed,
    /// or uncertain.
    /// </summary>
    public int RestorationsBlocked { get; }

    /// <summary>
    /// Creates a bounded lifecycle result.
    /// </summary>
    /// <param name="fence">The drained fence.</param>
    /// <param name="outcome">The bounded outcome.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="operationsReconciled">The number of reconciled operations.</param>
    /// <param name="restorationsCompleted">The number of completed restorations.</param>
    /// <param name="restorationsBlocked">The number of blocked restorations.</param>
    /// <returns>A lifecycle result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is undefined.</exception>
    public static ArtworkLifecycleResult Create(
        ArtworkLifecycleFence fence,
        ArtworkLifecycleOutcome outcome,
        string reason,
        int operationsReconciled = 0,
        int restorationsCompleted = 0,
        int restorationsBlocked = 0)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown lifecycle outcome.");
        }

        return new ArtworkLifecycleResult(
            fence,
            outcome,
            ArtworkOperationErrors.Sanitize(reason) ?? "The lifecycle drain did not complete.",
            operationsReconciled,
            restorationsCompleted,
            restorationsBlocked);
    }
}
