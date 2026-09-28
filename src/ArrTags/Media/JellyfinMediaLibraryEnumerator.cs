using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// <remarks>
/// The pinned Jellyfin 12 host orders the candidate query by <c>SortName</c>
/// ascending then raw <c>Name</c> ascending and offers no keyset or after-key
/// predicate, so the resume walks the host-ordered pages in bounded
/// <c>maxItems</c> pages until it locates the anchor by its unique identifier and
/// then returns the page that begins strictly after it (ADR-029 clause 2). The
/// walk uses only the supported <c>StartIndex</c>/<c>Limit</c> paging surface,
/// checks cancellation between and inside pages, and yields between pages so a
/// scheduled run stays bounded (ADR-022 clause 7).
/// </remarks>
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

    /// <inheritdoc />
    public async Task<MediaLibraryResumeResult> ResumeCandidatesAsync(
        Guid anchorItemId,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (anchorItemId == Guid.Empty)
        {
            return MediaLibraryResumeResult.AnchorNotFound();
        }

        var pageSize = maxItems < 1 ? 1 : maxItems;
        var offset = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = EnumerateCandidates(offset, pageSize)
                ?? Array.Empty<BaseItem>();

            for (var index = 0; index < page.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = page[index];
                if (item is not null && item.Id == anchorItemId)
                {
                    // ADR-022 clause 2: the run resumes at the first uncovered
                    // item, so the page begins at the anchor's absolute index + 1
                    // rather than at the next page boundary.
                    var startIndex = offset + index + 1;
                    var candidates = EnumerateCandidates(startIndex, pageSize)
                        ?? Array.Empty<BaseItem>();

                    return MediaLibraryResumeResult.Located(startIndex, candidates);
                }
            }

            if (page.Count < pageSize)
            {
                // The end of the host order was reached without locating the
                // anchor: it was removed, became ineligible, or the order changed.
                return MediaLibraryResumeResult.AnchorNotFound();
            }

            offset += page.Count;

            // Yield between pages so the bounded worker and the scheduler stay
            // responsive during the linear locate walk.
            await Task.Yield();
        }
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
