using System;

namespace ArrTags.Artwork;

/// <summary>
/// The immutable result of one reconciliation attempt. It carries the bounded
/// outcome, a bounded non-secret reason, the durable operation identifier when
/// one was reconciled, and the committed or updated artwork state when the
/// reconciliation produced one. It never contains a path, credential, entity, or
/// provider payload.
/// </summary>
public sealed class ArtworkReconciliationResult
{
    private ArtworkReconciliationResult(
        ArtworkReconciliationOutcome outcome,
        string reason,
        string? operationId,
        PublishedArtworkState? state)
    {
        Outcome = outcome;
        Reason = reason;
        OperationId = operationId;
        State = state;
    }

    /// <summary>
    /// Gets the bounded reconciliation outcome.
    /// </summary>
    public ArtworkReconciliationOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether the operation reached the committed final state.
    /// </summary>
    public bool IsCommitted => Outcome is ArtworkReconciliationOutcome.Resumed
        or ArtworkReconciliationOutcome.Completed
        or ArtworkReconciliationOutcome.CommittedFinalState;

    /// <summary>
    /// Gets a bounded, non-secret explanation of the outcome.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the durable operation identifier when one was reconciled, or <see langword="null"/>.
    /// </summary>
    public string? OperationId { get; }

    /// <summary>
    /// Gets the committed or updated artwork state when the reconciliation produced one, or <see langword="null"/>.
    /// </summary>
    public PublishedArtworkState? State { get; }

    /// <summary>
    /// Creates a reconciliation result.
    /// </summary>
    /// <param name="outcome">The bounded outcome.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="operationId">The durable operation identifier when one was reconciled.</param>
    /// <param name="state">The committed or updated artwork state when one was produced.</param>
    /// <returns>A reconciliation result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is undefined.</exception>
    public static ArtworkReconciliationResult Create(
        ArtworkReconciliationOutcome outcome,
        string reason,
        string? operationId = null,
        PublishedArtworkState? state = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown reconciliation outcome.");
        }

        return new ArtworkReconciliationResult(
            outcome,
            ArtworkOperationErrors.Sanitize(reason) ?? "The reconciliation did not complete.",
            operationId,
            state);
    }
}
