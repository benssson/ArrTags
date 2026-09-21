using System;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Updates;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The pure, synchronous relevance policy applied by the library-event entry
/// boundary. It converts an observed change into a bounded, provider-neutral
/// work hint only when the change can plausibly affect a V1 badge: an enabled
/// Arr provider exists, the item is a supported badge-bearing type, and the
/// change is not an image-only or ArrTags-generated internal update. It performs
/// no external I/O, no rendering, and no image write; configured library scope
/// and the current item state are re-validated by the worker that consumes the
/// hint.
/// </summary>
public static class LibraryEventRelevance
{
    /// <summary>
    /// Attempts to convert a library change into a bounded work hint.
    /// </summary>
    /// <param name="change">The observed library change.</param>
    /// <param name="configuration">The current public configuration snapshot.</param>
    /// <param name="hint">The bounded work hint when the change is relevant.</param>
    /// <returns><see langword="true"/> when the change should be enqueued.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool TryCreateHint(
        LibraryItemChangedEventArgs change,
        PluginConfigurationSnapshot configuration,
        out LibraryWorkHint hint)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(configuration);

        hint = default;

        if (change.ItemId == Guid.Empty || change.Origin == LibraryItemChangeOrigin.Image)
        {
            // An image-only update cannot change badge-relevant provider
            // metadata, and ArrTags' own publication raises exactly this origin,
            // so ignoring it prevents a self-triggered update loop.
            return false;
        }

        if (change.Reason != LibraryWorkReason.Removed
            && change.ItemType is not (MediaItemType.Movie or MediaItemType.Episode))
        {
            // V1 badge surfaces are Movie and Episode posters only (ADR-006).
            // A removal is still relevant because a previously published
            // surface may need reconciliation.
            return false;
        }

        if (!configuration.SonarrEnabled && !configuration.RadarrEnabled)
        {
            // No enabled provider can produce a badge, so no work is relevant.
            return false;
        }

        hint = new LibraryWorkHint(change.ItemId, change.Reason, configuration.ConfigurationVersion);
        return true;
    }
}
