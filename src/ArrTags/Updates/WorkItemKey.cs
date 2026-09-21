using System;
using System.Collections.Generic;
using ArrTags.Artwork;

namespace ArrTags.Updates;

/// <summary>
/// The coalescing and single-flight identity of one unit of update work: the
/// Jellyfin item, the resolved Arr connection when it is known, and the image
/// surface. A Jellyfin library event cannot identify the Arr connection, so a
/// hint-derived key carries <see langword="null"/> for the connection and the
/// unindexed <see cref="ArtworkImageSurface.Primary"/> surface; a
/// connection-scoped trigger supplies both. The key contains no credential,
/// provider DTO, path, or unbounded payload, so it is safe to retain as a queue
/// key and diagnostic.
/// </summary>
public readonly struct WorkItemKey : IEquatable<WorkItemKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemKey"/> struct.
    /// </summary>
    /// <param name="itemId">The non-empty Jellyfin item identifier.</param>
    /// <param name="connectionId">
    /// The resolved connection scope, or <see langword="null"/> when the
    /// connection is not yet known.
    /// </param>
    /// <param name="surface">The image surface the work applies to.</param>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The item identifier is empty.</exception>
    public WorkItemKey(Guid itemId, string? connectionId, ArtworkImageSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("A work item key requires a non-empty Jellyfin item identifier.", nameof(itemId));
        }

        ItemId = itemId;
        ConnectionId = string.IsNullOrWhiteSpace(connectionId) ? null : connectionId;
        Surface = surface;
    }

    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the resolved connection scope, or <see langword="null"/> when the
    /// connection is not yet known. It is the stable, non-secret connection
    /// identity, never a credential.
    /// </summary>
    public string? ConnectionId { get; }

    /// <summary>
    /// Gets the image surface the work applies to.
    /// </summary>
    public ArtworkImageSurface Surface { get; }

    /// <summary>
    /// Determines whether two keys are equal.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns><see langword="true"/> when every key field is equal.</returns>
    public static bool operator ==(WorkItemKey left, WorkItemKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two keys differ.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns><see langword="true"/> when any key field differs.</returns>
    public static bool operator !=(WorkItemKey left, WorkItemKey right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Determines whether this key equals another key.
    /// </summary>
    /// <param name="other">The other key.</param>
    /// <returns><see langword="true"/> when every key field is equal.</returns>
    public bool Equals(WorkItemKey other)
    {
        return ItemId == other.ItemId
            && string.Equals(ConnectionId, other.ConnectionId, StringComparison.Ordinal)
            && EqualityComparer<ArtworkImageSurface>.Default.Equals(Surface, other.Surface);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is WorkItemKey other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ItemId, ConnectionId, Surface);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var surface = Surface is null ? "*" : Surface.Key;
        return FormattableString.Invariant(
            $"WorkItemKey({ItemId:N}, {ConnectionId ?? "*"}, {surface})");
    }

    /// <summary>
    /// Creates the unresolved key for a bounded library work hint.
    /// </summary>
    /// <param name="hint">The bounded library work hint.</param>
    /// <returns>A key with no connection scope and the unindexed Primary surface.</returns>
    public static WorkItemKey FromHint(in LibraryWorkHint hint)
    {
        return new WorkItemKey(hint.ItemId, null, ArtworkImageSurface.Primary);
    }
}
