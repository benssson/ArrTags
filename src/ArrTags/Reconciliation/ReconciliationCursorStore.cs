using System;
using System.Collections.Generic;
using System.IO;
using ArrTags.State;

namespace ArrTags.Reconciliation;

/// <summary>
/// Persists and reads the single bounded <see cref="ReconciliationCursor"/>
/// record through the versioned cache state boundary (ADR-022 clause 4). The
/// record kind is the fixed constant <see cref="RecordKind"/> and the record
/// identifier is the fixed constant <see cref="RecordId"/>. The cursor is
/// rebuildable cache state (<see cref="StateAuthority.Cache"/>): a missing,
/// torn, or semantically invalid record is observed as a miss and resets the
/// run to the start of the candidate order rather than being quarantined or
/// failing the run. The record carries only the scope identity and the opaque
/// <c>(SortName, itemId)</c> position; the write is atomic and bounded, and it
/// never blocks a library-event handler because only the whole-scope triggers
/// read or write it.
/// </summary>
public sealed class ReconciliationCursorStore
{
    /// <summary>
    /// The fixed cache state record kind for the reconciliation cursor.
    /// </summary>
    public const string RecordKind = "reconciliation-cursor";

    /// <summary>
    /// The fixed record identifier of the single reconciliation cursor record.
    /// </summary>
    public const string RecordId = "current";

    private readonly StateRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconciliationCursorStore"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository under the plugin data root.</param>
    /// <exception cref="ArgumentNullException">The repository is <see langword="null"/>.</exception>
    public ReconciliationCursorStore(StateRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>
    /// Reads the persisted cursor for a scope. A record whose scope differs from
    /// the supplied scope resets to the start (ADR-022 clause 5); a missing,
    /// torn, or semantically invalid record is reported as missing or discarded
    /// for the same reset, and a cache read failure is treated as a miss.
    /// </summary>
    /// <param name="scope">The current reconciliation scope identity.</param>
    /// <returns>The bounded read result.</returns>
    /// <exception cref="ArgumentNullException">The scope is <see langword="null"/>.</exception>
    public ReconciliationCursorReadResult Read(ReconciliationScopeIdentity scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        StateReadResult<ReconciliationCursor> result;
        try
        {
            result = _repository.Read<ReconciliationCursor>(StateAuthority.Cache, RecordKind, RecordId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The cursor is rebuildable cache state; a failed read is a miss and
            // resets the run to the start rather than failing it.
            return ReconciliationCursorReadResult.Discarded();
        }

        if (result.Status == StateReadStatus.Missing)
        {
            return ReconciliationCursorReadResult.Missing();
        }

        if (result.Status != StateReadStatus.Found || result.Value is null)
        {
            return ReconciliationCursorReadResult.Discarded();
        }

        if (!result.Value.Validate(out _))
        {
            // The repository only discards records that fail the envelope
            // integrity check; a valid envelope whose payload violates the
            // documented bounds is discarded here and observed as a miss.
            TryDiscard();
            return ReconciliationCursorReadResult.Discarded();
        }

        if (!scope.Matches(result.Value))
        {
            return ReconciliationCursorReadResult.ScopeChanged();
        }

        if (result.Value.ItemId is not Guid itemId)
        {
            return ReconciliationCursorReadResult.Wrapped();
        }

        return ReconciliationCursorReadResult.Resume(new ReconciliationCursorPosition(result.Value.SortName, itemId));
    }

    /// <summary>
    /// Writes the cursor for a scope atomically as rebuildable cache state. A
    /// null position records the wrapped start of the candidate order; the
    /// <c>SortName</c> hint is truncated to the documented bound because it is a
    /// diagnostic hint and the unique item identifier is the anchor.
    /// </summary>
    /// <param name="scope">The current reconciliation scope identity.</param>
    /// <param name="position">The last covered position, or <see langword="null"/> to record the wrapped start.</param>
    /// <exception cref="ArgumentNullException">The scope is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The scope or position violates a documented bound.</exception>
    public void Write(ReconciliationScopeIdentity scope, ReconciliationCursorPosition? position)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var record = new ReconciliationCursor(
            ReconciliationCursor.CurrentCursorVersion,
            new List<string>(scope.EnabledLibraryIds),
            scope.MoviePosters,
            scope.EpisodePosters,
            position is { } value ? BoundSortName(value.SortName) : null,
            position?.ItemId);

        if (!record.Validate(out var reason))
        {
            throw new ArgumentException(
                string.Concat("The reconciliation cursor is invalid: ", reason),
                nameof(position));
        }

        _repository.Write(StateAuthority.Cache, RecordKind, RecordId, record);
    }

    private static string BoundSortName(string sortName)
    {
        return sortName.Length <= ReconciliationCursor.MaxSortNameHintLength
            ? sortName
            : sortName[..ReconciliationCursor.MaxSortNameHintLength];
    }

    private void TryDiscard()
    {
        try
        {
            var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, RecordKind, RecordId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
