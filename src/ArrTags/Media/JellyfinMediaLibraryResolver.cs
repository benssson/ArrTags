using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace ArrTags.Media;

/// <summary>
/// Resolves library and item context through Jellyfin's supported
/// <see cref="ILibraryManager"/> facilities. It performs read-only lookups and
/// holds no state of its own.
/// </summary>
public sealed class JellyfinMediaLibraryResolver : IMediaLibraryResolver
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinMediaLibraryResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager to read.</param>
    /// <exception cref="ArgumentNullException">The library manager is <see langword="null"/>.</exception>
    public JellyfinMediaLibraryResolver(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
    }

    /// <inheritdoc />
    public Guid? ResolveLibraryId(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Folder? fallback = null;
        foreach (var folder in _libraryManager.GetCollectionFolders(item))
        {
            if (folder.Id == Guid.Empty)
            {
                continue;
            }

            if (folder is CollectionFolder)
            {
                return folder.Id;
            }

            fallback ??= folder;
        }

        return fallback?.Id;
    }

    /// <inheritdoc />
    public BaseItem? ResolveItem(Guid itemId)
    {
        return itemId == Guid.Empty ? null : _libraryManager.GetItemById(itemId);
    }
}
