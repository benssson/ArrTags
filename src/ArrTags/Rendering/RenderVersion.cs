using ArrTags.Metadata;

namespace ArrTags.Rendering;

/// <summary>
/// The code-owned renderer and badge-schema identities that participate in the
/// render fingerprint. A change to any output-affecting rendering behavior
/// requires a renderer version change; a change to the meaning or availability
/// of badge metadata fields requires a badge schema version change. Neither
/// version is user-selectable.
/// </summary>
public static class RenderVersion
{
    /// <summary>
    /// The current renderer implementation version. It changes whenever layout,
    /// drawing, typography, contrast, or encoding behavior changes. Version 2
    /// corrects the EXIF dimension-swapping orientation transforms so an opaque
    /// source is no longer clipped or made partly transparent (task 4.11).
    /// Version 3 is the v1.1 coordinated advance (ADR-017 clause 6 and ADR-019
    /// clause 6): the per-selector value allowlist and the configurable global
    /// badge position and size change the rendered output.
    /// </summary>
    public const int CurrentRendererVersion = 3;

    /// <summary>
    /// The current badge schema version, which is owned by
    /// <see cref="BadgeMetadata.CurrentSchemaVersion"/>.
    /// </summary>
    public const int CurrentBadgeSchemaVersion = BadgeMetadata.CurrentSchemaVersion;
}
