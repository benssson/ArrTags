using System;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The immutable, bounded result of one artwork generation attempt. A published
/// result carries the committed <see cref="PublishedArtworkState"/> and the
/// durable operation identifier; every preservation outcome carries a bounded,
/// non-secret reason plus the safe renderer/reader classification that caused it.
/// It never contains source bytes, a partial artifact, a path, a provider
/// payload, a Jellyfin entity, or a credential.
/// </summary>
public sealed class ArtworkGenerationResult
{
    private ArtworkGenerationResult(
        ArtworkGenerationOutcome outcome,
        string reason,
        ArtworkSourceReadFailureReason? sourceFailureReason,
        RenderPassThroughReason? passThroughReason,
        RenderFailureReason? failureReason,
        ArtworkPublicationOutcome? publicationOutcome,
        string? operationId,
        PublishedArtworkState? state)
    {
        Outcome = outcome;
        Reason = reason;
        SourceFailureReason = sourceFailureReason;
        PassThroughReason = passThroughReason;
        FailureReason = failureReason;
        PublicationOutcome = publicationOutcome;
        OperationId = operationId;
        State = state;
    }

    /// <summary>
    /// Gets the bounded generation outcome.
    /// </summary>
    public ArtworkGenerationOutcome Outcome { get; }

    /// <summary>
    /// Gets a value indicating whether the derived image was published and
    /// committed.
    /// </summary>
    public bool Published => Outcome == ArtworkGenerationOutcome.Published;

    /// <summary>
    /// Gets a value indicating whether the currently usable artwork was left
    /// unchanged. It is the inverse of <see cref="Published"/>.
    /// </summary>
    public bool Preserved => !Published;

    /// <summary>
    /// Gets a bounded, non-secret explanation of the outcome.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the bounded source-read failure classification when the outcome is
    /// <see cref="ArtworkGenerationOutcome.SourceUnavailable"/>, or
    /// <see langword="null"/>.
    /// </summary>
    public ArtworkSourceReadFailureReason? SourceFailureReason { get; }

    /// <summary>
    /// Gets the safe render pass-through reason when the outcome is
    /// <see cref="ArtworkGenerationOutcome.RenderPassThrough"/>, or
    /// <see langword="null"/>.
    /// </summary>
    public RenderPassThroughReason? PassThroughReason { get; }

    /// <summary>
    /// Gets the safe render failure reason when the outcome is
    /// <see cref="ArtworkGenerationOutcome.RenderFailed"/>, or
    /// <see langword="null"/>.
    /// </summary>
    public RenderFailureReason? FailureReason { get; }

    /// <summary>
    /// Gets the bounded publication outcome when a complete render reached
    /// publication, or <see langword="null"/> when publication was not attempted.
    /// </summary>
    public ArtworkPublicationOutcome? PublicationOutcome { get; }

    /// <summary>
    /// Gets the durable operation identifier when a write-ahead publication
    /// operation was created, or <see langword="null"/>.
    /// </summary>
    public string? OperationId { get; }

    /// <summary>
    /// Gets the committed published artwork state when the outcome is
    /// <see cref="ArtworkGenerationOutcome.Published"/>, or <see langword="null"/>.
    /// </summary>
    public PublishedArtworkState? State { get; }

    /// <summary>
    /// Creates a published result from a successful publication.
    /// </summary>
    /// <param name="publication">The successful publication result.</param>
    /// <returns>A published generation result.</returns>
    /// <exception cref="ArgumentNullException">The publication is <see langword="null"/>.</exception>
    public static ArtworkGenerationResult Success(ArtworkPublicationResult publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!publication.Published || publication.State is null)
        {
            return Blocked("The publication did not report a committed published state.");
        }

        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.Published,
            publication.Reason,
            null,
            null,
            null,
            publication.Outcome,
            publication.OperationId,
            publication.State);
    }

    /// <summary>
    /// Creates an absent-source result. No render or image mutation occurred.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>An absent-source result.</returns>
    public static ArtworkGenerationResult NoSource(string reason)
    {
        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.NoSource,
            Repair(reason, "The image surface has no source artwork to render."),
            null,
            null,
            null,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a source-unavailable result. No render or image mutation occurred.
    /// </summary>
    /// <param name="sourceFailureReason">The bounded source-read classification, when known.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A source-unavailable result.</returns>
    public static ArtworkGenerationResult SourceUnavailable(
        ArtworkSourceReadFailureReason? sourceFailureReason,
        string reason)
    {
        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.SourceUnavailable,
            Repair(reason, "The active source image could not be read."),
            sourceFailureReason,
            null,
            null,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a render pass-through result. The current artwork is preserved and
    /// no publication occurred.
    /// </summary>
    /// <param name="passThroughReason">The safe render pass-through reason.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A render pass-through result.</returns>
    public static ArtworkGenerationResult PassThrough(
        RenderPassThroughReason passThroughReason,
        string reason)
    {
        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.RenderPassThrough,
            Repair(reason, "The render passed through and the current artwork is preserved."),
            null,
            passThroughReason,
            null,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a render-failed result. The current artwork is preserved and no
    /// publication occurred.
    /// </summary>
    /// <param name="failureReason">The safe render failure reason.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A render-failed result.</returns>
    public static ArtworkGenerationResult RenderFailed(
        RenderFailureReason failureReason,
        string reason)
    {
        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.RenderFailed,
            Repair(reason, "The render failed and the current artwork is preserved."),
            null,
            null,
            failureReason,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a publication-not-completed result. The current artwork is
    /// preserved.
    /// </summary>
    /// <param name="publication">The bounded publication result that did not publish.</param>
    /// <returns>A publication-not-completed result.</returns>
    /// <exception cref="ArgumentNullException">The publication is <see langword="null"/>.</exception>
    public static ArtworkGenerationResult PublicationNotCompleted(ArtworkPublicationResult publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.PublicationNotCompleted,
            Repair(publication.Reason, "The publication did not complete and the current artwork is preserved."),
            null,
            null,
            null,
            publication.Outcome,
            publication.OperationId,
            null);
    }

    /// <summary>
    /// Creates a blocked result. The current artwork is preserved.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A blocked result.</returns>
    public static ArtworkGenerationResult Blocked(string reason)
    {
        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.Blocked,
            Repair(reason, "The artwork generation was blocked and the current artwork is preserved."),
            null,
            null,
            null,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a blocked result for a publication that was refused before any
    /// image mutation (for example an unresolved durable operation or an
    /// ineligible subject). The current artwork is preserved and the bounded
    /// publication outcome is retained for diagnostics.
    /// </summary>
    /// <param name="publication">The bounded publication result that was blocked.</param>
    /// <returns>A blocked result.</returns>
    /// <exception cref="ArgumentNullException">The publication is <see langword="null"/>.</exception>
    public static ArtworkGenerationResult BlockedByPublication(ArtworkPublicationResult publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.Blocked,
            Repair(publication.Reason, "The artwork generation was blocked and the current artwork is preserved."),
            null,
            null,
            null,
            publication.Outcome,
            publication.OperationId,
            null);
    }

    /// <summary>
    /// Creates a cancelled result. No partial artifact or publication occurred.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A cancelled result.</returns>
    public static ArtworkGenerationResult Cancelled(string reason)
    {
        return new ArtworkGenerationResult(
            ArtworkGenerationOutcome.Cancelled,
            Repair(reason, "The artwork generation was cancelled."),
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static string Repair(string? reason, string fallback)
    {
        return ArtworkOperationErrors.Sanitize(reason) ?? fallback;
    }
}
