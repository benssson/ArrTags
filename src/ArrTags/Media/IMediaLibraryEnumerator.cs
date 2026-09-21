using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace ArrTags.Media;

/// <summary>
/// The ArrTags boundary for enumerating the candidate V1 badge-bearing Jellyfin
/// items (movies and episodes) in bounded pages. It keeps the scheduled and
/// post-scan reconciliation independent of the concrete Jellyfin library manager
/// so the batching, cancellation, and enqueue behavior is unit-testable without a
/// live host. The boundary applies no library-scope or eligibility policy; the
/// caller filters each page through <see cref="MediaIdentityFactory"/> and
/// <see cref="MediaEligibility"/> against the current configuration snapshot.
/// </summary>
public interface IMediaLibraryEnumerator
{
    /// <summary>
    /// Gets the total number of candidate movie and episode items. It is a
    /// best-effort progress denominator and is never used to bound work.
    /// </summary>
    /// <returns>The total candidate count.</returns>
    int CountCandidates();

    /// <summary>
    /// Returns one bounded page of candidate items in a deterministic order.
    /// </summary>
    /// <param name="startIndex">The zero-based start index.</param>
    /// <param name="maxItems">The maximum page size; a value below one is clamped to one.</param>
    /// <returns>The candidate items for the page.</returns>
    IReadOnlyList<BaseItem> EnumerateCandidates(int startIndex, int maxItems);
}
