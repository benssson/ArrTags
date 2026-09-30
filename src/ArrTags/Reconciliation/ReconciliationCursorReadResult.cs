using System;

namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded result of reading the persisted reconciliation cursor for a
/// scope. The status explains why the run starts at the beginning or resumes;
/// <see cref="Position"/> is present only for
/// <see cref="ReconciliationCursorStatus.Position"/>.
/// </summary>
public sealed class ReconciliationCursorReadResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReconciliationCursorReadResult"/> class.
    /// </summary>
    /// <param name="status">The bounded read status.</param>
    /// <param name="position">The recorded position, when the status is <see cref="ReconciliationCursorStatus.Position"/>.</param>
    public ReconciliationCursorReadResult(ReconciliationCursorStatus status, ReconciliationCursorPosition? position)
    {
        Status = status;
        Position = position;
    }

    /// <summary>
    /// Gets the bounded read status.
    /// </summary>
    public ReconciliationCursorStatus Status { get; }

    /// <summary>
    /// Gets the recorded position when the status is
    /// <see cref="ReconciliationCursorStatus.Position"/>; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public ReconciliationCursorPosition? Position { get; }

    /// <summary>
    /// Gets a value indicating whether the result carries a resume position.
    /// </summary>
    public bool HasPosition => Position is not null;

    /// <summary>
    /// Creates the result for a missing record.
    /// </summary>
    /// <returns>The missing result.</returns>
    public static ReconciliationCursorReadResult Missing()
    {
        return new ReconciliationCursorReadResult(ReconciliationCursorStatus.Missing, null);
    }

    /// <summary>
    /// Creates the result for a discarded invalid or torn cache record.
    /// </summary>
    /// <returns>The discarded result.</returns>
    public static ReconciliationCursorReadResult Discarded()
    {
        return new ReconciliationCursorReadResult(ReconciliationCursorStatus.Discarded, null);
    }

    /// <summary>
    /// Creates the result for a valid record whose scope differs from the
    /// current scope.
    /// </summary>
    /// <returns>The scope-changed result.</returns>
    public static ReconciliationCursorReadResult ScopeChanged()
    {
        return new ReconciliationCursorReadResult(ReconciliationCursorStatus.ScopeChanged, null);
    }

    /// <summary>
    /// Creates the result for a valid record at the wrapped start.
    /// </summary>
    /// <returns>The wrapped result.</returns>
    public static ReconciliationCursorReadResult Wrapped()
    {
        return new ReconciliationCursorReadResult(ReconciliationCursorStatus.Wrapped, null);
    }

    /// <summary>
    /// Creates the result for a valid record with a resume position.
    /// </summary>
    /// <param name="position">The recorded position.</param>
    /// <returns>The position result.</returns>
    /// <exception cref="ArgumentException">The position is undefined.</exception>
    public static ReconciliationCursorReadResult Resume(ReconciliationCursorPosition position)
    {
        if (position.ItemId == Guid.Empty)
        {
            throw new ArgumentException("A resume position requires a non-empty item identifier.", nameof(position));
        }

        return new ReconciliationCursorReadResult(ReconciliationCursorStatus.Position, position);
    }
}
