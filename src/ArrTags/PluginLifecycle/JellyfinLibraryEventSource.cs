using System;
using System.Threading;
using ArrTags.Media;
using ArrTags.Updates;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// Adapts Jellyfin's <see cref="ILibraryManager"/> change events to the
/// ArrTags <see cref="ILibraryEventSource"/> boundary and unsubscribes when
/// disposed. The mapping is synchronous and in-memory: it reads only the item
/// identity, structural type, and update reason already present on the Jellyfin
/// event arguments and performs no provider, rendering, image, or library
/// lookup.
/// </summary>
public sealed class JellyfinLibraryEventSource : ILibraryEventSource, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLibraryEventSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager to observe.</param>
    public JellyfinLibraryEventSource(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));

        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
    }

    /// <inheritdoc />
    public event EventHandler<LibraryItemChangedEventArgs>? ItemAdded;

    /// <inheritdoc />
    public event EventHandler<LibraryItemChangedEventArgs>? ItemUpdated;

    /// <inheritdoc />
    public event EventHandler<LibraryItemChangedEventArgs>? ItemRemoved;

    /// <summary>
    /// Maps a Jellyfin change event to the ArrTags boundary without performing
    /// any I/O. It is public so the mapping rules can be verified without a live
    /// host; it is not part of the runtime event flow.
    /// </summary>
    /// <param name="change">The Jellyfin change event arguments.</param>
    /// <param name="reason">The ArrTags reason for the observed change.</param>
    /// <returns>The bounded ArrTags change description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is <see langword="null"/>.</exception>
    public static LibraryItemChangedEventArgs MapChange(ItemChangeEventArgs change, LibraryWorkReason reason)
    {
        ArgumentNullException.ThrowIfNull(change);

        var item = change.Item;
        var origin = (change.UpdateReason & ItemUpdateType.ImageUpdate) != 0
            ? LibraryItemChangeOrigin.Image
            : LibraryItemChangeOrigin.Library;

        return new LibraryItemChangedEventArgs(item?.Id ?? Guid.Empty, reason, GetItemType(item), origin);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
    }

    private static MediaItemType? GetItemType(BaseItem? item)
    {
        return item switch
        {
            Movie => MediaItemType.Movie,
            Series => MediaItemType.Series,
            Season => MediaItemType.Season,
            Episode => MediaItemType.Episode,
            _ => null,
        };
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        ItemAdded?.Invoke(this, MapChange(e, LibraryWorkReason.Added));
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        ItemUpdated?.Invoke(this, MapChange(e, LibraryWorkReason.Updated));
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        ItemRemoved?.Invoke(this, MapChange(e, LibraryWorkReason.Removed));
    }
}
