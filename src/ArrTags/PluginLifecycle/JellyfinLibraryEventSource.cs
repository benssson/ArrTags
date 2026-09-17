using System;
using System.Threading;
using MediaBrowser.Controller.Library;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// Adapts Jellyfin's <see cref="ILibraryManager"/> change events to the
/// ArrTags <see cref="ILibraryEventSource"/> boundary and unsubscribes when
/// disposed.
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

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        ItemAdded?.Invoke(this, ToArgs(e));
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        ItemUpdated?.Invoke(this, ToArgs(e));
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        ItemRemoved?.Invoke(this, ToArgs(e));
    }

    private static LibraryItemChangedEventArgs ToArgs(ItemChangeEventArgs e)
    {
        return new LibraryItemChangedEventArgs(e.Item?.Id ?? Guid.Empty);
    }
}
