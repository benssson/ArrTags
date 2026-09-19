using System;

namespace ArrTags.Rendering;

/// <summary>
/// The bounded, non-secret outcome of one render attempt. A rendered result
/// carries a complete, bounded PNG artifact plus its oriented dimensions, output
/// hash, and deterministic output fingerprint. A pass-through or failed result
/// carries exactly one safe reason code and never a partial artifact, provider
/// payload, path, or credential.
/// </summary>
public sealed class RenderResult
{
    /// <summary>
    /// The MIME type of a rendered artifact.
    /// </summary>
    public const string PngContentType = "image/png";

    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private RenderResult(
        RenderStatus status,
        ReadOnlyMemory<byte> pngBytes,
        int width,
        int height,
        string? outputHash,
        string? outputFingerprint,
        RenderPassThroughReason? passThroughReason,
        RenderFailureReason? failureReason)
    {
        Status = status;
        PngBytes = pngBytes;
        Width = width;
        Height = height;
        OutputHash = outputHash;
        OutputFingerprint = outputFingerprint;
        PassThroughReason = passThroughReason;
        FailureReason = failureReason;
    }

    /// <summary>
    /// Gets the bounded outcome status.
    /// </summary>
    public RenderStatus Status { get; }

    /// <summary>
    /// Gets the complete PNG artifact, or an empty value when no artifact exists.
    /// </summary>
    public ReadOnlyMemory<byte> PngBytes { get; }

    /// <summary>
    /// Gets a value indicating whether a complete artifact is present.
    /// </summary>
    public bool HasArtifact => Status == RenderStatus.Rendered;

    /// <summary>
    /// Gets the artifact MIME type, or <see langword="null"/> when no artifact
    /// exists.
    /// </summary>
    public string? ContentType => HasArtifact ? PngContentType : null;

    /// <summary>
    /// Gets the oriented output width in pixels, or zero when no artifact exists.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the oriented output height in pixels, or zero when no artifact
    /// exists.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the uppercase SHA-256 of the artifact bytes, or <see langword="null"/>
    /// when no artifact exists.
    /// </summary>
    public string? OutputHash { get; }

    /// <summary>
    /// Gets the deterministic output fingerprint, or <see langword="null"/> when
    /// no artifact exists.
    /// </summary>
    public string? OutputFingerprint { get; }

    /// <summary>
    /// Gets the safe pass-through reason, or <see langword="null"/> when the
    /// status is not pass-through.
    /// </summary>
    public RenderPassThroughReason? PassThroughReason { get; }

    /// <summary>
    /// Gets the safe failure reason, or <see langword="null"/> when the status is
    /// not failed.
    /// </summary>
    public RenderFailureReason? FailureReason { get; }

    /// <summary>
    /// Creates a rendered result carrying one complete PNG artifact.
    /// </summary>
    /// <param name="pngBytes">The complete PNG bytes.</param>
    /// <param name="width">The oriented output width in pixels.</param>
    /// <param name="height">The oriented output height in pixels.</param>
    /// <param name="outputHash">The uppercase SHA-256 of the artifact.</param>
    /// <param name="outputFingerprint">The deterministic output fingerprint.</param>
    /// <returns>A rendered <see cref="RenderResult"/>.</returns>
    /// <exception cref="ArgumentException">The artifact is empty, not a PNG, or a required identity string is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    public static RenderResult Rendered(
        ReadOnlySpan<byte> pngBytes,
        int width,
        int height,
        string outputHash,
        string outputFingerprint)
    {
        if (pngBytes.Length < PngSignature.Length || !pngBytes[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            throw new ArgumentException("A rendered result requires a complete PNG artifact.", nameof(pngBytes));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentException.ThrowIfNullOrEmpty(outputHash);
        ArgumentException.ThrowIfNullOrEmpty(outputFingerprint);

        return new RenderResult(
            RenderStatus.Rendered,
            pngBytes.ToArray(),
            width,
            height,
            outputHash,
            outputFingerprint,
            null,
            null);
    }

    /// <summary>
    /// Creates a pass-through result for one safe reason.
    /// </summary>
    /// <param name="reason">The defined pass-through reason.</param>
    /// <returns>A pass-through <see cref="RenderResult"/> with no artifact.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The reason is not defined.</exception>
    public static RenderResult PassThrough(RenderPassThroughReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown pass-through reason.");
        }

        return new RenderResult(RenderStatus.PassThrough, default, 0, 0, null, null, reason, null);
    }

    /// <summary>
    /// Creates a failed result for one safe reason with no artifact.
    /// </summary>
    /// <param name="reason">The defined failure reason.</param>
    /// <returns>A failed <see cref="RenderResult"/> with no artifact.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The reason is not defined.</exception>
    public static RenderResult Failed(RenderFailureReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown failure reason.");
        }

        return new RenderResult(RenderStatus.Failed, default, 0, 0, null, null, null, reason);
    }
}
