using System;

namespace ArrTags.Artwork;

/// <summary>
/// The immutable result of one single-subject publication attempt. A published
/// result carries the committed <see cref="PublishedArtworkState"/> and its
/// operation identifier; every other outcome carries a bounded, non-secret
/// reason and never contains a path, credential, entity, or provider payload.
/// </summary>
public sealed class ArtworkPublicationResult
{
    private ArtworkPublicationResult(
        ArtworkPublicationOutcome outcome,
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
    /// Gets the bounded publication outcome.
    /// </summary>
    public ArtworkPublicationOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether the derived image was published and
    /// committed.
    /// </summary>
    public bool Published => Outcome == ArtworkPublicationOutcome.Published;

    /// <summary>
    /// Gets a bounded, non-secret explanation of the outcome.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the durable operation identifier when a write-ahead operation was
    /// created, or <see langword="null"/> when the attempt failed before it.
    /// </summary>
    public string? OperationId { get; }

    /// <summary>
    /// Gets the committed published artwork state when the outcome is
    /// <see cref="ArtworkPublicationOutcome.Published"/>, or <see langword="null"/>.
    /// </summary>
    public PublishedArtworkState? State { get; }

    /// <summary>
    /// Creates a successful published result.
    /// </summary>
    /// <param name="operationId">The durable operation identifier.</param>
    /// <param name="state">The committed published artwork state.</param>
    /// <returns>A published result.</returns>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    public static ArtworkPublicationResult Success(string operationId, PublishedArtworkState state)
    {
        ArgumentException.ThrowIfNullOrEmpty(operationId);
        ArgumentNullException.ThrowIfNull(state);

        return new ArtworkPublicationResult(
            ArtworkPublicationOutcome.Published,
            "The derived artwork was published and committed.",
            operationId,
            state);
    }

    /// <summary>
    /// Creates a bounded failed result.
    /// </summary>
    /// <param name="outcome">The failure outcome; must not be <see cref="ArtworkPublicationOutcome.Published"/>.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="operationId">The durable operation identifier when a write-ahead operation was created.</param>
    /// <returns>A failed result.</returns>
    public static ArtworkPublicationResult Failure(
        ArtworkPublicationOutcome outcome,
        string reason,
        string? operationId = null)
    {
        if (outcome == ArtworkPublicationOutcome.Published)
        {
            outcome = ArtworkPublicationOutcome.Blocked;
        }

        return new ArtworkPublicationResult(
            outcome,
            ArtworkOperationErrors.Sanitize(reason) ?? "The publication did not complete.",
            operationId,
            null);
    }
}
