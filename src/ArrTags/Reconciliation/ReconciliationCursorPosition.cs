using System;

namespace ArrTags.Reconciliation;

/// <summary>
/// The opaque, bounded <c>(SortName, itemId)</c> position of the last covered
/// candidate (ADR-022 clause 3 as amended by ADR-029). The <see cref="ItemId"/>
/// is the unique ArrTags-side identity anchor; <see cref="SortName"/> is a
/// boundary hint and diagnostic only, so it is never used to order or locate the
/// item. The position never carries an item path, provider payload, or
/// credential.
/// </summary>
public readonly struct ReconciliationCursorPosition : IEquatable<ReconciliationCursorPosition>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReconciliationCursorPosition"/> struct.
    /// </summary>
    /// <param name="sortName">The boundary <c>SortName</c> hint; a null value is stored as an empty hint.</param>
    /// <param name="itemId">The non-empty unique anchor item identifier.</param>
    /// <exception cref="ArgumentException">The item identifier is empty.</exception>
    public ReconciliationCursorPosition(string? sortName, Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("A reconciliation cursor position requires a non-empty item identifier.", nameof(itemId));
        }

        SortName = sortName ?? string.Empty;
        ItemId = itemId;
    }

    /// <summary>
    /// Gets the boundary <c>SortName</c> hint. It may be empty when the host has
    /// no sort name for the item.
    /// </summary>
    public string SortName { get; }

    /// <summary>
    /// Gets the unique anchor item identifier.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Determines whether two positions are equal.
    /// </summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns><see langword="true"/> when every position field is equal.</returns>
    public static bool operator ==(ReconciliationCursorPosition left, ReconciliationCursorPosition right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two positions differ.
    /// </summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns><see langword="true"/> when any position field differs.</returns>
    public static bool operator !=(ReconciliationCursorPosition left, ReconciliationCursorPosition right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Determines whether this position equals another position.
    /// </summary>
    /// <param name="other">The other position.</param>
    /// <returns><see langword="true"/> when every position field is equal.</returns>
    public bool Equals(ReconciliationCursorPosition other)
    {
        return ItemId == other.ItemId
            && string.Equals(SortName, other.SortName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ReconciliationCursorPosition other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ItemId, SortName);
    }
}
