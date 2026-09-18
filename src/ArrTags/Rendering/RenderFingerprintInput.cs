using System;
using ArrTags.Media;

namespace ArrTags.Rendering;

/// <summary>
/// The validated, immutable snapshot of every output-affecting value for one
/// render attempt. It is a provider-neutral value object: it contains only
/// canonical fingerprints, resolved badge values, a code-owned or
/// configuration-derived output policy, and the renderer and badge schema
/// versions. It never contains a provider DTO, credential, path, correlation
/// identifier, or timestamp, so equal inputs always produce equal fingerprints.
/// </summary>
public sealed class RenderFingerprintInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderFingerprintInput"/> class.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier scoping the request.</param>
    /// <param name="itemType">The Movie or Episode item type that carries the poster surface.</param>
    /// <param name="sourceImageFingerprint">The non-empty fingerprint of the exact retained source artwork.</param>
    /// <param name="sourceWidth">The oriented source width in pixels.</param>
    /// <param name="sourceHeight">The oriented source height in pixels.</param>
    /// <param name="metadataFingerprint">The canonical metadata fingerprint, or <see langword="null"/> when no metadata is available.</param>
    /// <param name="configurationFingerprint">The secret-free output-affecting configuration fingerprint.</param>
    /// <param name="selection">The ordered resolved badge values.</param>
    /// <param name="outputPolicy">The effective output policy.</param>
    /// <param name="rendererVersion">The renderer implementation version.</param>
    /// <param name="badgeSchemaVersion">The badge schema version.</param>
    /// <exception cref="ArgumentException">The item identifier, a fingerprint, the configuration fingerprint, or a policy value is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The item type is undefined or a dimension or version is not positive.</exception>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public RenderFingerprintInput(
        Guid jellyfinItemId,
        MediaItemType itemType,
        string sourceImageFingerprint,
        int sourceWidth,
        int sourceHeight,
        string? metadataFingerprint,
        string configurationFingerprint,
        BadgeSelection selection,
        RenderOutputPolicy outputPolicy,
        int rendererVersion = RenderVersion.CurrentRendererVersion,
        int badgeSchemaVersion = RenderVersion.CurrentBadgeSchemaVersion)
    {
        if (jellyfinItemId == Guid.Empty)
        {
            throw new ArgumentException("A render fingerprint input requires a non-empty Jellyfin item identifier.", nameof(jellyfinItemId));
        }

        if (!Enum.IsDefined(itemType))
        {
            throw new ArgumentOutOfRangeException(nameof(itemType), itemType, "Unknown media item type.");
        }

        ArgumentException.ThrowIfNullOrEmpty(sourceImageFingerprint);
        ArgumentException.ThrowIfNullOrEmpty(configurationFingerprint);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(outputPolicy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rendererVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(badgeSchemaVersion);

        if (metadataFingerprint is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(metadataFingerprint);
        }

        JellyfinItemId = jellyfinItemId;
        ItemType = itemType;
        SourceImageFingerprint = sourceImageFingerprint;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        MetadataFingerprint = metadataFingerprint;
        ConfigurationFingerprint = configurationFingerprint;
        Selection = selection;
        OutputPolicy = outputPolicy;
        RendererVersion = rendererVersion;
        BadgeSchemaVersion = badgeSchemaVersion;
    }

    /// <summary>
    /// Gets the Jellyfin item identifier scoping the request.
    /// </summary>
    public Guid JellyfinItemId { get; }

    /// <summary>
    /// Gets the Movie or Episode item type that carries the poster surface. V1
    /// has one supported surface, the unindexed Primary poster.
    /// </summary>
    public MediaItemType ItemType { get; }

    /// <summary>
    /// Gets the fingerprint of the exact retained source artwork.
    /// </summary>
    public string SourceImageFingerprint { get; }

    /// <summary>
    /// Gets the oriented source width in pixels.
    /// </summary>
    public int SourceWidth { get; }

    /// <summary>
    /// Gets the oriented source height in pixels.
    /// </summary>
    public int SourceHeight { get; }

    /// <summary>
    /// Gets the canonical metadata fingerprint, or <see langword="null"/> when
    /// no metadata is available.
    /// </summary>
    public string? MetadataFingerprint { get; }

    /// <summary>
    /// Gets the secret-free output-affecting configuration fingerprint.
    /// </summary>
    public string ConfigurationFingerprint { get; }

    /// <summary>
    /// Gets the ordered resolved badge values.
    /// </summary>
    public BadgeSelection Selection { get; }

    /// <summary>
    /// Gets the effective output policy.
    /// </summary>
    public RenderOutputPolicy OutputPolicy { get; }

    /// <summary>
    /// Gets the renderer implementation version.
    /// </summary>
    public int RendererVersion { get; }

    /// <summary>
    /// Gets the badge schema version.
    /// </summary>
    public int BadgeSchemaVersion { get; }
}
