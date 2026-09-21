using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace ArrTags.Media;

/// <summary>
/// Enumerates the candidate V1 badge-bearing items through Jellyfin's supported
/// read-only <see cref="ILibraryManager"/> query surface. Only movies and
/// episodes are candidates; library scope, location eligibility, and the badge
/// surface flags are applied by the caller, so this adapter performs no policy.
/// The enumeration is read-only and holds no state of its own.
/// </summary>
public sealed class JellyfinMediaLibraryEnumerator : IMediaLibraryEnumerator
{
    private static readonly BaseItemKind[] CandidateKinds = new[]
    {
        BaseItemKind.Movie,
        BaseItemKind.Episode,
    };

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinMediaLibraryEnumerator"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager to read.</param>
    /// <exception cref="ArgumentNullException">The library manager is <see langword="null"/>.</exception>
    public JellyfinMediaLibraryEnumerator(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
    }

    /// <inheritdoc />
    public int CountCandidates()
    {
        return _libraryManager.GetCount(CreateQuery());
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItem> EnumerateCandidates(int startIndex, int maxItems)
    {
        var query = CreateQuery();
        query.StartIndex = startIndex < 0 ? 0 : startIndex;
        query.Limit = maxItems < 1 ? 1 : maxItems;
        return _libraryManager.GetItemList(query);
    }

    private static InternalItemsQuery CreateQuery()
    {
        return new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = CandidateKinds,

            // A deterministic order keeps paged enumeration from skipping or
            // repeating items when the library changes between pages.
            OrderBy = new[] { (ItemSortBy.SortName, SortOrder.Ascending) },
        };
    }
}
