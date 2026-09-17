using System;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// Describes a Jellyfin library item change observed at the ArrTags boundary.
/// </summary>
public sealed class LibraryItemChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryItemChangedEventArgs"/> class.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier.</param>
    public LibraryItemChangedEventArgs(Guid itemId)
    {
        ItemId = itemId;
    }

    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public Guid ItemId { get; }
}
