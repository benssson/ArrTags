using System;
using ArrTags.Media;
using ArrTags.Updates;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// Describes a Jellyfin library item change observed at the ArrTags boundary. It
/// carries only the bounded facts a short event handler needs to validate
/// relevance: the Jellyfin item identifier, the change reason, the Jellyfin
/// structural item type when it is known, and whether the change was an
/// image-only update. It never carries a provider DTO, path, credential, or the
/// Jellyfin item itself.
/// </summary>
public sealed class LibraryItemChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryItemChangedEventArgs"/> class.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier.</param>
    /// <param name="reason">The bounded reason the change was observed.</param>
    /// <param name="itemType">The Jellyfin structural item type, or <see langword="null"/> when it is unsupported or unknown.</param>
    /// <param name="origin">The origin of the change; defaults to a general library change.</param>
    public LibraryItemChangedEventArgs(
        Guid itemId,
        LibraryWorkReason reason,
        MediaItemType? itemType = null,
        LibraryItemChangeOrigin origin = LibraryItemChangeOrigin.Library)
    {
        ItemId = itemId;
        Reason = reason;
        ItemType = itemType;
        Origin = origin;
    }

    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the bounded reason this item is a candidate for update work.
    /// </summary>
    public LibraryWorkReason Reason { get; }

    /// <summary>
    /// Gets the Jellyfin structural item type, or <see langword="null"/> when the
    /// item is not one of the supported Movie, Series, Season, or Episode types.
    /// </summary>
    public MediaItemType? ItemType { get; }

    /// <summary>
    /// Gets the origin of the change. An image-only update is ignored by the
    /// entry boundary because it cannot change badge-relevant provider metadata
    /// and is raised by ArrTags' own image publication.
    /// </summary>
    public LibraryItemChangeOrigin Origin { get; }
}
