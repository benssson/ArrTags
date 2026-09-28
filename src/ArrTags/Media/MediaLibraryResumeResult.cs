using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace ArrTags.Media;

/// <summary>
/// The bounded result of an identity-anchored resume request
/// (<see cref="IMediaLibraryEnumerator.ResumeCandidatesAsync"/>). When the
/// anchor is located, <see cref="StartIndex"/> is the absolute offset in the
/// host order of the first candidate strictly after the located anchor item and
/// <see cref="Candidates"/> is the bounded page that begins at that offset. An
/// empty page at a non-zero start offset means the anchor was the last
/// candidate, so the order wraps to the start.
/// </summary>
public sealed class MediaLibraryResumeResult
{
    private MediaLibraryResumeResult(
        MediaLibraryResumeOutcome outcome,
        int startIndex,
        IReadOnlyList<BaseItem> candidates)
    {
        Outcome = outcome;
        StartIndex = startIndex;
        Candidates = candidates;
    }

    /// <summary>
    /// Gets the bounded resume outcome.
    /// </summary>
    public MediaLibraryResumeOutcome Outcome { get; }

    /// <summary>
    /// Gets the absolute offset of the first candidate strictly after the
    /// located anchor item. It is zero when the anchor cannot be located.
    /// </summary>
    public int StartIndex { get; }

    /// <summary>
    /// Gets the bounded candidate page that begins strictly after the located
    /// anchor item; empty when the anchor cannot be located or was the last
    /// candidate in the order.
    /// </summary>
    public IReadOnlyList<BaseItem> Candidates { get; }

    /// <summary>
    /// Creates the result for an anchor that is not present in the current host
    /// order.
    /// </summary>
    /// <returns>The anchor-not-found result.</returns>
    public static MediaLibraryResumeResult AnchorNotFound()
    {
        return new MediaLibraryResumeResult(
            MediaLibraryResumeOutcome.AnchorNotFound,
            0,
            Array.Empty<BaseItem>());
    }

    /// <summary>
    /// Creates the result for a located anchor.
    /// </summary>
    /// <param name="startIndex">The absolute offset of the first candidate strictly after the anchor; clamped to zero.</param>
    /// <param name="candidates">The bounded page that begins at that offset.</param>
    /// <returns>The located result.</returns>
    /// <exception cref="ArgumentNullException">The page is <see langword="null"/>.</exception>
    public static MediaLibraryResumeResult Located(int startIndex, IReadOnlyList<BaseItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return new MediaLibraryResumeResult(
            MediaLibraryResumeOutcome.Located,
            startIndex < 0 ? 0 : startIndex,
            candidates);
    }
}
