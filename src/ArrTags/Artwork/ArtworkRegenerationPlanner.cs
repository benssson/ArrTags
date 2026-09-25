using System;
using System.Collections.Generic;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral Phase 6 publication-fingerprint gate. After metadata state
/// is published for a subject it decides whether artwork must be (re)generated and
/// published, following <c>docs/architecture/08-reconciliation-and-update-flow.md</c> step 7 and <c>docs/architecture/09-persisted-artwork-rendering.md</c>
/// steps 2-4.
/// </summary>
/// <remarks>
/// The logical publication fingerprint covers the source-artwork identity (the
/// retained original source for an owned session), the canonical metadata
/// fingerprint, the secret-free renderer configuration fingerprint, the resolved
/// badge selection, and the renderer/badge-schema versions. It is computed with
/// the same <see cref="RenderFingerprint"/> and <see cref="BadgeDefinitionResolver"/>
/// the renderer uses, so an unchanged fingerprint is a reliable no-op. The gate
/// never reads the active image and never mutates anything; it only compares
/// authoritative state.
/// <para>
/// A subject with no ownership session, or with a terminal session
/// (<see cref="ArtworkPublicationState.Restored"/> or
/// <see cref="ArtworkPublicationState.Removed"/>), always requires a new
/// recoverable source baseline: the coordinator observes and captures the active
/// surface, which is the exact source that will be retained. A subject in a
/// blocked or in-flight ownership state is never automatically regenerated, and a
/// session without a retained present source cannot be re-rendered.
/// </para>
/// </remarks>
public static class ArtworkRegenerationPlanner
{
    /// <summary>
    /// Decides whether the supplied subject must be (re)generated.
    /// </summary>
    /// <param name="publishedState">The persisted ownership state, or <see langword="null"/> when no session exists.</param>
    /// <param name="identity">The current canonical Jellyfin item identity.</param>
    /// <param name="metadata">The current normalized metadata observation, or <see langword="null"/>.</param>
    /// <param name="metadataUsable">Whether the metadata state is usable as current per the task 6.5 freshness policy.</param>
    /// <param name="badgeDefinitions">The ordered resolved badge definitions.</param>
    /// <param name="outputPolicy">The effective renderer output policy.</param>
    /// <param name="configurationFingerprint">The secret-free renderer configuration fingerprint.</param>
    /// <param name="rendererVersion">The current renderer implementation version.</param>
    /// <param name="badgeSchemaVersion">The current badge schema version.</param>
    /// <returns>The bounded regeneration decision.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The configuration fingerprint is empty.</exception>
    public static ArtworkRegenerationDecision Decide(
        PublishedArtworkState? publishedState,
        MediaIdentity identity,
        BadgeMetadata? metadata,
        bool metadataUsable,
        IReadOnlyList<BadgeDefinition> badgeDefinitions,
        RenderOutputPolicy outputPolicy,
        string configurationFingerprint,
        int rendererVersion,
        int badgeSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(badgeDefinitions);
        ArgumentNullException.ThrowIfNull(outputPolicy);
        ArgumentException.ThrowIfNullOrEmpty(configurationFingerprint);

        if (identity.ItemType is not (MediaItemType.Movie or MediaItemType.Episode))
        {
            return ArtworkRegenerationDecision.Skip("The item type has no V1 badge surface.");
        }

        if (!metadataUsable)
        {
            return ArtworkRegenerationDecision.Skip(
                "The metadata state is not usable as current; the current artwork is retained.");
        }

        if (publishedState is not null && IsPublicationBlocked(publishedState.State))
        {
            return ArtworkRegenerationDecision.Skip(
                "The current artwork ownership state does not permit automatic publication.");
        }

        if (publishedState is { SourcePresence: ArtworkImagePresence.Present }
            && publishedState.State is ArtworkPublicationState.Published or ArtworkPublicationState.NotPublished)
        {
            if (publishedState.SourceFingerprint is null
                || publishedState.SourceCaptureIdentity is not { Width: > 0, Height: > 0 })
            {
                return ArtworkRegenerationDecision.Skip(
                    "The retained source baseline is incomplete; the current artwork is retained.");
            }

            if (publishedState.State == ArtworkPublicationState.Published)
            {
                var desired = ComputeDesiredFingerprint(
                    publishedState,
                    identity,
                    metadata,
                    badgeDefinitions,
                    outputPolicy,
                    configurationFingerprint,
                    rendererVersion,
                    badgeSchemaVersion);

                if (publishedState.RendererVersion == rendererVersion
                    && string.Equals(desired, publishedState.PublishedFingerprint, StringComparison.Ordinal))
                {
                    return ArtworkRegenerationDecision.Skip(
                        "The publication fingerprint is unchanged; no artwork work is required.");
                }

                return ArtworkRegenerationDecision.Generate(
                    "The publication fingerprint changed; the retained original source is re-rendered.",
                    desired);
            }

            return ArtworkRegenerationDecision.Generate(
                "A captured source baseline is ready to publish.");
        }

        if (publishedState is { SourcePresence: ArtworkImagePresence.Absent }
            && publishedState.State is ArtworkPublicationState.Published or ArtworkPublicationState.NotPublished)
        {
            // An absent baseline means the surface had no original image to draw
            // on; re-observing the active surface would only recover the previous
            // ArrTags output, so the current artwork is retained.
            return ArtworkRegenerationDecision.Skip(
                "The published session has no retained original source to render from.");
        }

        return ArtworkRegenerationDecision.Generate(
            "No owned source baseline exists; a new recoverable source baseline is required.");
    }

    /// <summary>
    /// Computes the logical publication fingerprint the current output-affecting
    /// inputs would produce for an owned session's retained source. It is the
    /// exact value the renderer would store on a successful publication.
    /// </summary>
    /// <param name="publishedState">The owned session supplying the retained source identity.</param>
    /// <param name="identity">The current canonical Jellyfin item identity.</param>
    /// <param name="metadata">The current normalized metadata observation, or <see langword="null"/>.</param>
    /// <param name="badgeDefinitions">The ordered resolved badge definitions.</param>
    /// <param name="outputPolicy">The effective renderer output policy.</param>
    /// <param name="configurationFingerprint">The secret-free renderer configuration fingerprint.</param>
    /// <param name="rendererVersion">The current renderer implementation version.</param>
    /// <param name="badgeSchemaVersion">The current badge schema version.</param>
    /// <returns>The uppercase SHA-256 output fingerprint.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The retained source baseline is incomplete.</exception>
    public static string ComputeDesiredFingerprint(
        PublishedArtworkState publishedState,
        MediaIdentity identity,
        BadgeMetadata? metadata,
        IReadOnlyList<BadgeDefinition> badgeDefinitions,
        RenderOutputPolicy outputPolicy,
        string configurationFingerprint,
        int rendererVersion,
        int badgeSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(publishedState);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(badgeDefinitions);
        ArgumentNullException.ThrowIfNull(outputPolicy);

        if (publishedState.SourceFingerprint is null
            || publishedState.SourceCaptureIdentity is not { Width: > 0, Height: > 0 } capture)
        {
            throw new InvalidOperationException("The retained source baseline is incomplete.");
        }

        var selection = BadgeDefinitionResolver.Resolve(metadata, badgeDefinitions);
        var input = new RenderFingerprintInput(
            identity.JellyfinItemId,
            identity.ItemType,
            publishedState.SourceFingerprint,
            capture.Width!.Value,
            capture.Height!.Value,
            metadata?.MetadataFingerprint,
            configurationFingerprint,
            selection,
            outputPolicy,
            rendererVersion,
            badgeSchemaVersion);

        return RenderFingerprint.ComputeOutputFingerprint(input);
    }

    private static bool IsPublicationBlocked(ArtworkPublicationState state)
    {
        return state is ArtworkPublicationState.OwnershipLost
            or ArtworkPublicationState.OwnershipUnknown
            or ArtworkPublicationState.RestorePending
            or ArtworkPublicationState.RestoreBlocked;
    }
}
