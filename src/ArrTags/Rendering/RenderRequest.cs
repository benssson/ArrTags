using System;
using System.Collections.Generic;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;

namespace ArrTags.Rendering;

/// <summary>
/// The complete provider-neutral logical input for one render attempt. It
/// carries the validated source descriptor, the canonical item and match
/// identities, optional canonical metadata, the ordered definition and output
/// policy snapshots, the secret-free configuration fingerprint, and the renderer
/// and badge schema identities. It never contains a provider DTO, credential,
/// filesystem path, or Jellyfin entity.
/// </summary>
public sealed class RenderRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderRequest"/> class.
    /// </summary>
    /// <param name="sourceImage">The retained source descriptor, or <see langword="null"/> when the source is unavailable.</param>
    /// <param name="mediaIdentity">The canonical Jellyfin item identity.</param>
    /// <param name="match">The canonical match result scoping the metadata.</param>
    /// <param name="metadata">The optional canonical metadata observation.</param>
    /// <param name="badgeDefinitions">The ordered provider-neutral definition snapshot.</param>
    /// <param name="configurationFingerprint">The secret-free output-affecting configuration fingerprint.</param>
    /// <param name="outputPolicy">The effective code-owned output policy.</param>
    /// <param name="limits">The accepted operational limits; the request owns an independent copy.</param>
    /// <param name="rendererVersion">The renderer implementation version.</param>
    /// <param name="badgeSchemaVersion">The badge schema version.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The definition list is empty or the configuration fingerprint is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A version is not positive.</exception>
    public RenderRequest(
        SourceImageInput? sourceImage,
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
        ArgumentNullException.ThrowIfNull(mediaIdentity);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(badgeDefinitions);
        ArgumentException.ThrowIfNullOrEmpty(configurationFingerprint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rendererVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(badgeSchemaVersion);

        if (badgeDefinitions.Count == 0)
        {
            throw new ArgumentException("A render request requires at least one badge definition.", nameof(badgeDefinitions));
        }

        var definitions = new List<BadgeDefinition>(badgeDefinitions.Count);
        foreach (var definition in badgeDefinitions)
        {
            definitions.Add(definition);
        }

        SourceImage = sourceImage;
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
    /// Gets the retained source descriptor, or <see langword="null"/> when the
    /// source is unavailable and the render must pass through.
    /// </summary>
    public SourceImageInput? SourceImage { get; }

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
    /// Gets the optional canonical metadata observation. It is used only when the
    /// match is eligible.
    /// </summary>
    public BadgeMetadata? Metadata { get; }

    /// <summary>
    /// Gets the ordered provider-neutral definition snapshot.
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
    /// Gets the accepted operational limits used to bound the work. The request
    /// owns an independent copy.
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
