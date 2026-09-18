using System;
using MediaBrowser.Controller.Entities;

namespace ArrTags.Media;

/// <summary>
/// The ArrTags boundary for the Jellyfin library lookups needed to snapshot a
/// <see cref="MediaIdentity"/>. It keeps the snapshot builder independent of the
/// concrete Jellyfin library manager so matching logic remains unit-testable
/// without a live host.
/// </summary>
public interface IMediaLibraryResolver
{
    /// <summary>
    /// Resolves the owning collection-folder/library identifier for an item.
    /// </summary>
    /// <param name="item">The item to locate.</param>
    /// <returns>The collection-folder identifier, or <see langword="null"/> when no library is known.</returns>
    Guid? ResolveLibraryId(BaseItem item);

    /// <summary>
    /// Resolves a Jellyfin item by identifier.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier.</param>
    /// <returns>The item, or <see langword="null"/> when it is absent.</returns>
    BaseItem? ResolveItem(Guid itemId);
}
