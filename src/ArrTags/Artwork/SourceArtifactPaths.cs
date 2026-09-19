using System;
using System.IO;

namespace ArrTags.Artwork;

/// <summary>
/// Resolves traversal-safe paths for the content-addressed source-artifact
/// store. An identifier must be a 64-character hexadecimal SHA-256, so it can
/// never contain a path separator or traversal sequence, and the bytes are
/// sharded by the first hash byte to keep directories bounded.
/// </summary>
public sealed class SourceArtifactPaths
{
    private const string ArtifactsDirectoryName = "artifacts";
    private const string SourceDirectoryName = "source";

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceArtifactPaths"/> class.
    /// </summary>
    /// <param name="rootPath">The plugin-owned data root.</param>
    public SourceArtifactPaths(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        _root = Path.GetFullPath(rootPath);
    }

    /// <summary>
    /// Gets the absolute plugin data root.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Gets the absolute source-artifact directory.
    /// </summary>
    /// <returns>The source-artifact directory path.</returns>
    public string GetArtifactDirectory()
    {
        return Path.Combine(_root, ArtifactsDirectoryName, SourceDirectoryName);
    }

    /// <summary>
    /// Gets the absolute bytes path for an artifact identifier.
    /// </summary>
    /// <param name="artifactId">The 64-character hexadecimal artifact identifier.</param>
    /// <returns>The absolute artifact bytes path.</returns>
    /// <exception cref="ArgumentException">The identifier is not a SHA-256.</exception>
    public string GetArtifactPath(string artifactId)
    {
        if (!IsValidArtifactId(artifactId))
        {
            throw new ArgumentException("A source artifact identifier must be a 64-character SHA-256 hex value.", nameof(artifactId));
        }

        var normalized = NormalizeArtifactId(artifactId);
        var shard = normalized[..2].ToLowerInvariant();
        return Path.Combine(GetArtifactDirectory(), shard, string.Concat(normalized, ".bin"));
    }

    /// <summary>
    /// Determines whether the value is a valid content-addressed artifact identifier.
    /// </summary>
    /// <param name="value">The candidate identifier.</param>
    /// <returns><see langword="true"/> when the value is a 64-character SHA-256.</returns>
    public static bool IsValidArtifactId(string? value)
    {
        return ArtworkHashes.IsSha256Hex(value);
    }

    /// <summary>
    /// Normalizes an artifact identifier to upper-case hexadecimal.
    /// </summary>
    /// <param name="artifactId">The artifact identifier.</param>
    /// <returns>The upper-case identifier.</returns>
    public static string NormalizeArtifactId(string artifactId)
    {
        ArgumentException.ThrowIfNullOrEmpty(artifactId);
        return artifactId.ToUpperInvariant();
    }
}
