using System;
using System.Collections.Generic;
using System.Globalization;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// Persists and reads the durable <see cref="ArtworkOperation"/> write-ahead
/// record through the versioned authoritative state boundary. The record is
/// integrity-tagged, atomically written, and quarantined on corruption; it is
/// never treated as ordinary cache and is never blindly replayed. The store is
/// keyed per Jellyfin item and image surface so only one operation can exist for
/// a subject at a time, and it fences writes by the monotonic generation so a
/// stale queued operation can never overwrite a newer durable record.
/// </summary>
public sealed class ArtworkOperationStore
{
    /// <summary>
    /// The authoritative state record kind for artwork operations.
    /// </summary>
    public const string RecordKind = "artwork-operation";

    private readonly StateRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkOperationStore"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository under the plugin data root.</param>
    /// <exception cref="ArgumentNullException">The repository is <see langword="null"/>.</exception>
    public ArtworkOperationStore(StateRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>
    /// Gets the stable, traversal-safe state record identifier for an item and surface.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface.</param>
    /// <returns>The record identifier.</returns>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    public static string GetRecordId(Guid jellyfinItemId, ArtworkImageSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return string.Concat(
            jellyfinItemId.ToString("N", CultureInfo.InvariantCulture),
            "-",
            surface.Key);
    }

    /// <summary>
    /// Reads the durable operation for an item and surface.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface.</param>
    /// <returns>The operation read outcome.</returns>
    public StateReadResult<ArtworkOperation> Read(Guid jellyfinItemId, ArtworkImageSurface surface)
    {
        return Read(GetRecordId(jellyfinItemId, surface));
    }

    /// <summary>
    /// Reads the durable operation for a record identifier. A valid envelope
    /// whose payload violates the documented invariants is quarantined and
    /// reported as invalid rather than returned as current state.
    /// </summary>
    /// <param name="recordId">The state record identifier.</param>
    /// <returns>The operation read outcome.</returns>
    public StateReadResult<ArtworkOperation> Read(string recordId)
    {
        ArgumentException.ThrowIfNullOrEmpty(recordId);

        StateReadResult<ArtworkOperation> result;
        try
        {
            result = _repository.Read<ArtworkOperation>(StateAuthority.Authoritative, RecordKind, recordId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Quarantine(recordId, "The artwork operation record could not be deserialized.");
        }

        if (result.Status != StateReadStatus.Found || result.Value is null)
        {
            return result;
        }

        if (result.Value.Validate(out var reason))
        {
            return result;
        }

        return Quarantine(recordId, reason);
    }

    /// <summary>
    /// Enumerates the valid durable operation records in a bounded,
    /// deterministic order. An invalid record is quarantined and omitted rather
    /// than returned as current state.
    /// </summary>
    /// <param name="maxRecords">The bounded maximum number of records; defaults to the repository bound.</param>
    /// <returns>The valid operation records.</returns>
    public IReadOnlyList<ArtworkOperation> Enumerate(int maxRecords = StateRepository.MaxEnumerationRecords)
    {
        return _repository.Enumerate<ArtworkOperation>(
            StateAuthority.Authoritative,
            RecordKind,
            maxRecords);
    }

    /// <summary>
    /// Writes an artwork operation atomically as authoritative state. The write
    /// is refused when the operation violates a documented invariant, when a
    /// stale generation would overwrite a newer durable record, or when a
    /// different operation would replace the subject's current generation.
    /// </summary>
    /// <param name="operation">The operation to persist.</param>
    /// <exception cref="ArgumentNullException">The operation is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The operation violates a documented invariant.</exception>
    /// <exception cref="InvalidOperationException">The write is refused by the generation fence or the phase rules.</exception>
    public void Write(ArtworkOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (!operation.Validate(out var reason))
        {
            throw new ArgumentException(
                string.Concat("The artwork operation is invalid: ", reason),
                nameof(operation));
        }

        var recordId = GetRecordId(operation.JellyfinItemId, operation.ImageSurface);
        var existing = Read(recordId);
        if (existing.Status == StateReadStatus.Found && existing.Value is not null)
        {
            EnsureWriteIsFenced(existing.Value, operation);
        }

        _repository.Write(
            StateAuthority.Authoritative,
            RecordKind,
            recordId,
            operation,
            operation.IsTerminal);
    }

    private static void EnsureWriteIsFenced(ArtworkOperation durable, ArtworkOperation candidate)
    {
        if (ArtworkOperationFencing.IsStale(durable.Generation, candidate.Generation))
        {
            throw new InvalidOperationException(
                "A stale artwork operation generation cannot overwrite a newer durable record.");
        }

        if (!ArtworkOperationFencing.IsSameGeneration(durable.Generation, candidate.Generation))
        {
            return;
        }

        if (!string.Equals(durable.OperationId, candidate.OperationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Only one non-terminal artwork operation may exist per item and image surface.");
        }

        if (durable.Phase != candidate.Phase
            && !ArtworkOperationPhases.TryAdvance(durable.Phase, candidate.Phase, out var transitionReason))
        {
            throw new InvalidOperationException(
                string.Concat("The artwork operation phase cannot advance: ", transitionReason));
        }
    }

    private StateReadResult<ArtworkOperation> Quarantine(string recordId, string reason)
    {
        StateQuarantine.TryQuarantine(
            _repository.Paths,
            StateAuthority.Authoritative,
            RecordKind,
            recordId,
            DateTimeOffset.UtcNow,
            reason,
            out _);
        return StateResults.Quarantined<ArtworkOperation>(reason);
    }
}
