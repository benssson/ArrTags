using System;

namespace ArrTags.Rendering;

/// <summary>
/// The bounded, non-secret outcome of enforcing image or text limits before
/// decode, draw, or encode work. An accepted result carries
/// <see cref="RenderLimitReason.None"/> and no output; a rejected result carries
/// exactly one safe reason code and never a partial artifact, provider payload,
/// path, or credential.
/// </summary>
public sealed class RenderLimitResult
{
    private RenderLimitResult(RenderLimitReason reason)
    {
        Reason = reason;
    }

    /// <summary>
    /// Gets the shared accepted result.
    /// </summary>
    public static RenderLimitResult Accepted { get; } = new RenderLimitResult(RenderLimitReason.None);

    /// <summary>
    /// Gets the safe reason code. <see cref="RenderLimitReason.None"/> means the
    /// limit was not violated.
    /// </summary>
    public RenderLimitReason Reason { get; }

    /// <summary>
    /// Gets a value indicating whether the limit was not violated.
    /// </summary>
    public bool IsAccepted => Reason == RenderLimitReason.None;

    /// <summary>
    /// Gets a value indicating whether the limit was violated and the render
    /// must not proceed.
    /// </summary>
    public bool IsRejected => !IsAccepted;

    /// <summary>
    /// Creates a rejected result for one safe reason code.
    /// </summary>
    /// <param name="reason">The non-<see cref="RenderLimitReason.None"/> reason.</param>
    /// <returns>A rejected <see cref="RenderLimitResult"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The reason is <see cref="RenderLimitReason.None"/> or is not defined.</exception>
    public static RenderLimitResult Rejected(RenderLimitReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown render limit reason.");
        }

        if (reason == RenderLimitReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A rejected result requires a non-None reason.");
        }

        return new RenderLimitResult(reason);
    }
}
