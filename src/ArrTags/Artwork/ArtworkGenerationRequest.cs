using System;
using System.Collections.Generic;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The canonical, caller-supplied input for generating one item's derived poster
/// artwork from its current source image. It carries the Jellyfin subject and V1
/// surface, the canonical match and optional metadata, the ordered definition and
/// output policy snapshots, the secret-free configuration fingerprint, the
/// accepted operational limits, and the renderer/schema versions. It carries no
/// source bytes, source artifact, provider DTO, credential, filesystem path,
/// Jellyfin entity, or network handle; the source is observed through the host
/// boundary by the coordinator so a caller can never inject content as a source.
/// </summary>
public sealed class ArtworkGenerationRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkGenerationRequest"/> class.
    /// </summary>
    /// <param name="jellyfinItemId">The target Jellyfin item identifier.</param>
    /// <param name="imageSurface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="mediaIdentity">The canonical Jellyfin item identity.</param>
    /// <param name="match">The canonical match result scoping the metadata.</param>
    /// <param name="metadata">The optional canonical metadata observation.</param>
    /// <param name="badgeDefinitions">The ordered provider-neutral definition snapshot.</param>
    /// <param name="configurationFingerprint">The secret-free output-affecting configuration fingerprint.</param>
    /// <param name="outputPolicy">The effective code-owned output policy, or the code default.</param>
    /// <param name="limits">The accepted operational limits, or the code default.</param>
    /// <param name="rendererVersion">The renderer implementation version.</param>
    /// <param name="badgeSchemaVersion">The badge schema version.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public ArtworkGenerationRequest(
        Guid jellyfinItemId,
        ArtworkImageSurface imageSurface,
        MediaIdentity mediaIdentity,
        MediaMatch match,
        BadgeMetadata? metadata,
        IReadOnlyList<BadgeDefinition> badgeDefinitions,
        string configurationFingerprint,
        RenderOutputPolicy? outputPolicy = null,
        OperationalLimits? limits = null,
        int rendererVersion = RenderVersion.CurrentRendererVersion,
        int badgeSchemaVersion = RenderVersion.CurrentBadgeSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(imageSurface);
        ArgumentNullException.ThrowIfNull(mediaIdentity);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(badgeDefinitions);

        var definitions = new List<BadgeDefinition>(badgeDefinitions.Count);
        foreach (var definition in badgeDefinitions)
        {
            definitions.Add(definition);
        }

        JellyfinItemId = jellyfinItemId;
        ImageSurface = imageSurface;
        MediaIdentity = mediaIdentity;
        Match = match;
        Metadata = metadata;
        BadgeDefinitions = definitions.AsReadOnly();
        ConfigurationFingerprint = configurationFingerprint;
        OutputPolicy = outputPolicy ?? RenderOutputPolicy.Default;
        Limits = (limits ?? new OperationalLimits()).Clone();
        RendererVersion = rendererVersion;
        BadgeSchemaVersion = badgeSchemaVersion;
    }

    /// <summary>
    /// Gets the target Jellyfin item identifier.
    /// </summary>
    public Guid JellyfinItemId { get; }

    /// <summary>
    /// Gets the target image surface.
    /// </summary>
    public ArtworkImageSurface ImageSurface { get; }

    /// <summary>
    /// Gets the canonical Jellyfin item identity that owns the poster surface.
    /// </summary>
    public MediaIdentity MediaIdentity { get; }

    /// <summary>
    /// Gets the canonical match result. Only a <c>Matched</c> status permits the
    /// supplied metadata to be rendered.
    /// </summary>
    public MediaMatch Match { get; }

    /// <summary>
    /// Gets the optional canonical metadata observation.
    /// </summary>
    public BadgeMetadata? Metadata { get; }

    /// <summary>
    /// Gets the ordered provider-neutral definition snapshot. The request owns an
    /// independent copy.
    /// </summary>
    public IReadOnlyList<BadgeDefinition> BadgeDefinitions { get; }

    /// <summary>
    /// Gets the secret-free output-affecting configuration fingerprint.
    /// </summary>
    public string ConfigurationFingerprint { get; }

    /// <summary>
    /// Gets the effective output policy.
    /// </summary>
    public RenderOutputPolicy OutputPolicy { get; }

    /// <summary>
    /// Gets the accepted operational limits. The request owns an independent copy.
    /// </summary>
    public OperationalLimits Limits { get; }

    /// <summary>
    /// Gets the renderer implementation version.
    /// </summary>
    public int RendererVersion { get; }

    /// <summary>
    /// Gets the badge schema version.
    /// </summary>
    public int BadgeSchemaVersion { get; }
}
