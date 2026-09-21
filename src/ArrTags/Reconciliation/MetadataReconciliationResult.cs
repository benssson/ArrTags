using System;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Updates;

namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded outcome of one reconciliation attempt. It carries the worker
/// classification plus, when metadata state was actually published, the live
/// canonical context that the Phase 6 artwork stage consumes: the current
/// provider-neutral item identity, the match, the optional normalized metadata,
/// and the published metadata state entry. The live context is the exact input
/// the state entry was built from, so the artwork fingerprint computed from it is
/// byte-for-byte the value the renderer would produce; no reconstruction from the
/// persisted snapshot is required.
/// </summary>
/// <remarks>
/// The result is only populated when the reconciliation published atomic metadata
/// state after re-validating the current item and configuration. A discarded,
/// failed, or retried attempt carries no context so the artwork stage performs no
/// work. It never contains a provider DTO, credential, filesystem path, or
/// unbounded payload.
/// </remarks>
public sealed class MetadataReconciliationResult
{
    private MetadataReconciliationResult(
        WorkProcessingResult processingResult,
        MetadataStateEntry? entry,
        MediaIdentity? identity,
        MediaMatch? match,
        BadgeMetadata? metadata)
    {
        ProcessingResult = processingResult;
        Entry = entry;
        Identity = identity;
        Match = match;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the worker classification of the attempt.
    /// </summary>
    public WorkProcessingResult ProcessingResult { get; }

    /// <summary>
    /// Gets the published metadata state entry, or <see langword="null"/> when no
    /// state was published.
    /// </summary>
    public MetadataStateEntry? Entry { get; }

    /// <summary>
    /// Gets the current provider-neutral Jellyfin item identity, or
    /// <see langword="null"/> when no state was published.
    /// </summary>
    public MediaIdentity? Identity { get; }

    /// <summary>
    /// Gets the current canonical match, or <see langword="null"/> when no state
    /// was published.
    /// </summary>
    public MediaMatch? Match { get; }

    /// <summary>
    /// Gets the current normalized metadata observation, or <see langword="null"/>
    /// when the match carried no metadata.
    /// </summary>
    public BadgeMetadata? Metadata { get; }

    /// <summary>
    /// Gets a value indicating whether atomic metadata state was published.
    /// </summary>
    public bool HasPublishedState => Entry is not null;

    /// <summary>
    /// Creates a result for an attempt that did not publish metadata state.
    /// </summary>
    /// <param name="processingResult">The worker classification.</param>
    /// <returns>A context-free result.</returns>
    /// <exception cref="ArgumentNullException">The processing result is <see langword="null"/>.</exception>
    public static MetadataReconciliationResult Processed(WorkProcessingResult processingResult)
    {
        ArgumentNullException.ThrowIfNull(processingResult);
        return new MetadataReconciliationResult(processingResult, null, null, null, null);
    }

    /// <summary>
    /// Creates a result for an attempt that published metadata state.
    /// </summary>
    /// <param name="processingResult">The worker classification.</param>
    /// <param name="entry">The published metadata state entry.</param>
    /// <param name="identity">The current Jellyfin item identity.</param>
    /// <param name="match">The current canonical match.</param>
    /// <param name="metadata">The current normalized metadata, or <see langword="null"/>.</param>
    /// <returns>A result carrying the live reconciliation context.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static MetadataReconciliationResult WithEntry(
        WorkProcessingResult processingResult,
        MetadataStateEntry entry,
        MediaIdentity identity,
        MediaMatch match,
        BadgeMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(processingResult);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(match);
        return new MetadataReconciliationResult(processingResult, entry, identity, match, metadata);
    }
}
