using System;

namespace ArrTags.Artwork;

/// <summary>
/// The immutable result of gating one unit of new work on the recovery of the
/// durable artwork operation for an item/image surface. It carries the bounded
/// outcome, the durable generation the new work must supersede, the resolved
/// operation identifier when one exists, a bounded non-secret reason, and the
/// underlying reconciliation result when recovery actually ran. It never
/// contains a path, credential, entity, or provider payload.
/// </summary>
public sealed class ArtworkRecoveryGateResult
{
    private ArtworkRecoveryGateResult(
        ArtworkRecoveryGateOutcome outcome,
        long generation,
        string? operationId,
        string reason,
        ArtworkReconciliationResult? reconciliation)
    {
        Outcome = outcome;
        Generation = generation;
        OperationId = operationId;
        Reason = reason;
        Reconciliation = reconciliation;
    }

    /// <summary>
    /// Gets the bounded gate outcome.
    /// </summary>
    public ArtworkRecoveryGateOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether new work for the subject may proceed. Only
    /// a subject with no durable operation, an already-terminal operation, or a
    /// recovered operation may proceed.
    /// </summary>
    public bool CanProceed => Outcome is ArtworkRecoveryGateOutcome.NoOperation
        or ArtworkRecoveryGateOutcome.AlreadyTerminal
        or ArtworkRecoveryGateOutcome.Recovered;

    /// <summary>
    /// Gets the durable generation now authoritative for the subject, or <c>0</c>
    /// when no durable operation exists. Accepted new work must create its
    /// operation at a strictly newer generation so a stale generation can never
    /// supersede this durable record.
    /// </summary>
    public long Generation { get; }

    /// <summary>
    /// Gets the resolved durable operation identifier, or <see langword="null"/>
    /// when no durable operation exists or the record is invalid.
    /// </summary>
    public string? OperationId { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation of the outcome.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the reconciliation result when recovery ran for a non-terminal
    /// operation, or <see langword="null"/> otherwise.
    /// </summary>
    public ArtworkReconciliationResult? Reconciliation { get; }

    /// <summary>
    /// Creates a recovery gate result.
    /// </summary>
    /// <param name="outcome">The bounded outcome.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="generation">The authoritative durable generation, or <c>0</c> when none exists.</param>
    /// <param name="operationId">The resolved durable operation identifier, or <see langword="null"/>.</param>
    /// <param name="reconciliation">The reconciliation result when recovery ran, or <see langword="null"/>.</param>
    /// <returns>A recovery gate result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is undefined or the generation is negative.</exception>
    public static ArtworkRecoveryGateResult Create(
        ArtworkRecoveryGateOutcome outcome,
        string reason,
        long generation = 0,
        string? operationId = null,
        ArtworkReconciliationResult? reconciliation = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown recovery gate outcome.");
        }

        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "A recovery gate generation cannot be negative.");
        }

        return new ArtworkRecoveryGateResult(
            outcome,
            generation,
            operationId,
            ArtworkOperationErrors.Sanitize(reason) ?? "The artwork recovery gate did not complete.",
            reconciliation);
    }
}
