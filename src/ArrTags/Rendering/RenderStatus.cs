namespace ArrTags.Rendering;

/// <summary>
/// The bounded outcome of one render attempt. A rendered result carries the
/// complete bounded PNG artifact; a pass-through result means the current artwork
/// is unchanged because there is nothing to draw; a failed result means the
/// attempt could not safely complete and no artifact is produced.
/// </summary>
public enum RenderStatus
{
    /// <summary>
    /// A complete, bounded PNG artifact was produced.
    /// </summary>
    Rendered,

    /// <summary>
    /// No badge is displayable, so the current artwork is left unchanged.
    /// </summary>
    PassThrough,

    /// <summary>
    /// The attempt failed safely and produced no artifact.
    /// </summary>
    Failed,
}
