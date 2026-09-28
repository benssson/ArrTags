using System;
using System.Collections.Generic;

namespace ArrTags.Reconciliation;

/// <summary>
/// The persisted reconciliation cursor payload (ADR-022 clauses 3 through 5, as
/// amended by ADR-029 clause 3). It carries only the bounded scope identity
/// (the sorted enabled-library identifier set and the supported item-type
/// surface scope) and the opaque <c>(SortName, itemId)</c> position of the last
/// covered candidate; it never carries an item path, provider payload, or
/// credential. The position is an ArrTags-side identity anchor located by
/// matching the unique <see cref="ItemId"/> in the host's own candidate order;
/// the <see cref="SortName"/> component is a boundary hint and diagnostic, not
/// the ordering authority. A record with no position is the wrapped start of
/// the candidate order.
/// </summary>
public sealed class ReconciliationCursor
{
    /// <summary>
    /// The current cursor payload version. A different version is a rebuildable
    /// cache record and is discarded rather than treated as current.
    /// </summary>
    public const int CurrentCursorVersion = 1;

    /// <summary>
    /// The bounded maximum number of enabled-library identifiers carried by a
    /// record.
    /// </summary>
    public const int MaxEnabledLibraryIds = 4096;

    /// <summary>
    /// The bounded maximum length of one enabled-library identifier.
    /// </summary>
    public const int MaxEnabledLibraryIdLength = 256;

    /// <summary>
    /// The bounded maximum length of the persisted <see cref="SortName"/> hint.
    /// The hint is truncated to this bound when a record is written.
    /// </summary>
    public const int MaxSortNameHintLength = 512;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconciliationCursor"/> class.
    /// </summary>
    /// <param name="cursorVersion">The cursor payload version.</param>
    /// <param name="enabledLibraryIds">The sorted enabled-library identifier set; an empty set means no library restriction.</param>
    /// <param name="moviePosters">Whether movie poster reconciliation is in scope.</param>
    /// <param name="episodePosters">Whether episode poster reconciliation is in scope.</param>
    /// <param name="sortName">The boundary <c>SortName</c> hint, or <see langword="null"/> when the record is the wrapped start.</param>
    /// <param name="itemId">The unique anchor item identifier, or <see langword="null"/> when the record is the wrapped start.</param>
    public ReconciliationCursor(
        int cursorVersion,
        IReadOnlyList<string>? enabledLibraryIds,
        bool moviePosters,
        bool episodePosters,
        string? sortName,
        Guid? itemId)
    {
        CursorVersion = cursorVersion;
        EnabledLibraryIds = enabledLibraryIds is null
            ? Array.Empty<string>()
            : new List<string>(enabledLibraryIds).AsReadOnly();
        MoviePosters = moviePosters;
        EpisodePosters = episodePosters;
        SortName = sortName;
        ItemId = itemId;
    }

    /// <summary>
    /// Gets the cursor payload version.
    /// </summary>
    public int CursorVersion { get; }

    /// <summary>
    /// Gets the sorted enabled-library identifier set the position is scoped to.
    /// </summary>
    public IReadOnlyList<string> EnabledLibraryIds { get; }

    /// <summary>
    /// Gets a value indicating whether movie poster reconciliation is in the
    /// recorded item-type scope.
    /// </summary>
    public bool MoviePosters { get; }

    /// <summary>
    /// Gets a value indicating whether episode poster reconciliation is in the
    /// recorded item-type scope.
    /// </summary>
    public bool EpisodePosters { get; }

    /// <summary>
    /// Gets the boundary <c>SortName</c> hint for the recorded position, or
    /// <see langword="null"/> when the record is the wrapped start.
    /// </summary>
    public string? SortName { get; }

    /// <summary>
    /// Gets the unique anchor item identifier, or <see langword="null"/> when
    /// the record is the wrapped start of the candidate order.
    /// </summary>
    public Guid? ItemId { get; }

    /// <summary>
    /// Validates the record's structural invariants. A record that violates
    /// them is a rebuildable cache record: it is discarded and observed as a
    /// miss so the cursor resets to the start rather than failing the run.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation when invalid.</param>
    /// <returns><see langword="true"/> when the record is valid.</returns>
    public bool Validate(out string reason)
    {
        if (CursorVersion != CurrentCursorVersion)
        {
            reason = "The reconciliation cursor has an incompatible cursor version.";
            return false;
        }

        if (EnabledLibraryIds.Count > MaxEnabledLibraryIds)
        {
            reason = "The reconciliation cursor carries too many enabled-library identifiers.";
            return false;
        }

        foreach (var libraryId in EnabledLibraryIds)
        {
            if (string.IsNullOrWhiteSpace(libraryId) || libraryId.Length > MaxEnabledLibraryIdLength)
            {
                reason = "The reconciliation cursor carries an invalid enabled-library identifier.";
                return false;
            }
        }

        if (ItemId is Guid itemId)
        {
            if (itemId == Guid.Empty)
            {
                reason = "The reconciliation cursor position requires a non-empty item identifier.";
                return false;
            }

            if (SortName is null || SortName.Length > MaxSortNameHintLength)
            {
                reason = "The reconciliation cursor position requires a bounded SortName hint.";
                return false;
            }
        }
        else if (SortName is not null)
        {
            reason = "A wrapped reconciliation cursor cannot carry a SortName hint without a position.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
