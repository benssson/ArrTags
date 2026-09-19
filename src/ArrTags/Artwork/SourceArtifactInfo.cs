using System;

namespace ArrTags.Artwork;

/// <summary>
/// The integrity metadata for one immutable, content-addressed retained source
/// artifact. The artifact identifier is the SHA-256 of the exact bytes, which
/// makes the store content-addressed and lets an identical source be promoted
/// idempotently. The record never contains the source bytes, a media path, or a
/// credential.
/// </summary>
public sealed class SourceArtifactInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SourceArtifactInfo"/> class.
    /// </summary>
    /// <param name="artifactId">The content-addressed artifact identifier.</param>
    /// <param name="contentType">The source MIME type.</param>
    /// <param name="byteLength">The exact source byte length.</param>
    /// <param name="sha256">The SHA-256 of the exact source bytes.</param>
    public SourceArtifactInfo(string artifactId, string contentType, long byteLength, string sha256)
    {
        ArtifactId = artifactId;
        ContentType = contentType;
        ByteLength = byteLength;
        Sha256 = sha256;
    }

    /// <summary>
    /// Gets the content-addressed artifact identifier.
    /// </summary>
    public string ArtifactId { get; }

    /// <summary>
    /// Gets the source MIME type.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the exact source byte length.
    /// </summary>
    public long ByteLength { get; }

    /// <summary>
    /// Gets the SHA-256 of the exact source bytes.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Validates the artifact metadata.
    /// </summary>
    /// <param name="reason">A bounded, non-secret failure explanation.</param>
    /// <returns><see langword="true"/> when the metadata is valid.</returns>
    public bool Validate(out string reason)
    {
        if (!ArtworkHashes.IsSha256Hex(ArtifactId))
        {
            reason = "A source artifact identifier must be a 64-character SHA-256 hex value.";
            return false;
        }

        if (!ArtworkHashes.IsSha256Hex(Sha256))
        {
            reason = "A source artifact requires a 64-character SHA-256 integrity hash.";
            return false;
        }

        if (!string.Equals(ArtifactId, Sha256, StringComparison.OrdinalIgnoreCase))
        {
            reason = "A content-addressed source artifact identifier must equal its SHA-256.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ContentType) || ContentType.Length > 128)
        {
            reason = "A source artifact requires a bounded MIME type.";
            return false;
        }

        if (ByteLength <= 0)
        {
            reason = "A source artifact must contain at least one byte.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
