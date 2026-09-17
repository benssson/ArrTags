using System;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// Publishes Jellyfin library item changes to the ArrTags lifecycle without
/// exposing the full Jellyfin library manager to lifecycle consumers.
/// </summary>
public interface ILibraryEventSource
{
    /// <summary>
    /// Occurs when a library item is added.
    /// </summary>
    event EventHandler<LibraryItemChangedEventArgs>? ItemAdded;

    /// <summary>
    /// Occurs when a library item is updated.
    /// </summary>
    event EventHandler<LibraryItemChangedEventArgs>? ItemUpdated;

    /// <summary>
    /// Occurs when a library item is removed.
    /// </summary>
    event EventHandler<LibraryItemChangedEventArgs>? ItemRemoved;
}
