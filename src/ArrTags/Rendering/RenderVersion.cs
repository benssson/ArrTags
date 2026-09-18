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
    /// drawing, typography, contrast, or encoding behavior changes.
    /// </summary>
    public const int CurrentRendererVersion = 1;

    /// <summary>
    /// The current badge schema version, which is owned by
    /// <see cref="BadgeMetadata.CurrentSchemaVersion"/>.
    /// </summary>
    public const int CurrentBadgeSchemaVersion = BadgeMetadata.CurrentSchemaVersion;
}
