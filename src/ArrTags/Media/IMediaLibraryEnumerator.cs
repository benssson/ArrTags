using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// Resumes the candidate order strictly after the supplied unique anchor item
    /// (ADR-022 clause 3 as amended by ADR-029 clause 2). The pinned host exposes
    /// no keyset/after-key predicate, so the resume walks the host-ordered pages
    /// from the start of the order in bounded pages until it locates the anchor
    /// by identity, then returns the page that begins strictly after it. The walk
    /// checks cancellation between and inside pages and yields between pages
    /// (ADR-022 clause 7), so the caller stays bounded. When the anchor cannot be
    /// located the result is <see cref="MediaLibraryResumeOutcome.AnchorNotFound"/>
    /// and the caller resets to the start (ADR-029 clause 4).
    /// </summary>
    /// <param name="anchorItemId">The non-empty unique anchor item identifier.</param>
    /// <param name="maxItems">The bounded page size; a value below one is clamped to one.</param>
    /// <param name="cancellationToken">The token that cancels the walk.</param>
    /// <returns>The bounded resume result.</returns>
    Task<MediaLibraryResumeResult> ResumeCandidatesAsync(
        Guid anchorItemId,
        int maxItems,
        CancellationToken cancellationToken);
}
