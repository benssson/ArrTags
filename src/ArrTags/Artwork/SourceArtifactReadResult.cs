using System;

namespace ArrTags.Artwork;

/// <summary>
/// The outcome of reading a retained source artifact. A missing artifact is a
/// normal condition; a corrupt one is authoritative and must never be treated
/// as absent or silently rebuilt from a different source.
/// </summary>
public enum SourceArtifactReadStatus
{
    /// <summary>A valid artifact was found and passed integrity validation.</summary>
    Found,

    /// <summary>No artifact exists for the identifier.</summary>
    Missing,

    /// <summary>The identifier or manifest is malformed.</summary>
    Invalid,

    /// <summary>The artifact bytes are missing or failed integrity validation.</summary>
    Corrupt,
}

/// <summary>
/// The immutable result of reading one retained source artifact.
/// </summary>
public sealed class SourceArtifactReadResult
{
    private SourceArtifactReadResult(
        SourceArtifactReadStatus status,
        SourceArtifactInfo? info,
        ReadOnlyMemory<byte> bytes,
        string? reason)
    {
        Status = status;
        Info = info;
        Bytes = bytes;
        Reason = reason;
    }

    /// <summary>
    /// Gets the read status.
    /// </summary>
    public SourceArtifactReadStatus Status { get; }

    /// <summary>
    /// Gets the artifact metadata when the status is <see cref="SourceArtifactReadStatus.Found"/>.
    /// </summary>
    public SourceArtifactInfo? Info { get; }

    /// <summary>
    /// Gets the exact artifact bytes when the status is <see cref="SourceArtifactReadStatus.Found"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>
    /// Gets a bounded, non-secret explanation for an invalid or corrupt artifact.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Creates a found result.
    /// </summary>
    /// <param name="info">The artifact metadata.</param>
    /// <param name="bytes">The exact artifact bytes.</param>
    /// <returns>A found result.</returns>
    public static SourceArtifactReadResult Found(SourceArtifactInfo info, ReadOnlyMemory<byte> bytes)
    {
        return new SourceArtifactReadResult(SourceArtifactReadStatus.Found, info, bytes, null);
    }

    /// <summary>
    /// Creates a missing result.
    /// </summary>
    /// <returns>A missing result.</returns>
    public static SourceArtifactReadResult Missing()
    {
        return new SourceArtifactReadResult(SourceArtifactReadStatus.Missing, null, ReadOnlyMemory<byte>.Empty, null);
    }

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>An invalid result.</returns>
    public static SourceArtifactReadResult Invalid(string reason)
    {
        return new SourceArtifactReadResult(SourceArtifactReadStatus.Invalid, null, ReadOnlyMemory<byte>.Empty, reason);
    }

    /// <summary>
    /// Creates a corrupt result.
    /// </summary>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <returns>A corrupt result.</returns>
    public static SourceArtifactReadResult Corrupt(string reason)
    {
        return new SourceArtifactReadResult(SourceArtifactReadStatus.Corrupt, null, ReadOnlyMemory<byte>.Empty, reason);
    }
}
