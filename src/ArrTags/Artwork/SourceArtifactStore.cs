using System;
using System.Collections.Generic;
using System.IO;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// The authoritative, content-addressed, immutable store for retained source
/// artwork. The exact source bytes live under the plugin data folder and their
/// MIME type, byte length, and SHA-256 integrity metadata are persisted as an
/// authoritative state manifest through the versioned state boundary, so a
/// corrupt manifest is quarantined rather than silently rebuilt.
/// </summary>
/// <remarks>
/// Promotion writes the bytes to a flushed temporary file and atomically
/// replaces the destination only after bounded size, MIME/magic-byte format, and
/// hash validation. An identical artifact is promoted idempotently. The store
/// never evicts provenance as ordinary cache: when the authoritative storage
/// quota would be exceeded, new work is rejected and the current artwork is
/// preserved (ADR-004). An absent baseline is an explicit state and creates no
/// artifact.
/// </remarks>
public sealed class SourceArtifactStore
{
    /// <summary>
    /// The authoritative state record kind for source-artifact manifests.
    /// </summary>
    public const string ManifestKind = "source-artifact";

    private readonly StateRepository _repository;
    private readonly SourceArtifactPaths _paths;
    private readonly long _sourceArtifactLimitBytes;
    private readonly long _artifactStorageQuotaBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceArtifactStore"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository that owns the plugin data root and limits.</param>
    /// <exception cref="ArgumentNullException">The repository is <see langword="null"/>.</exception>
    public SourceArtifactStore(StateRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _paths = new SourceArtifactPaths(repository.Paths.Root);
        _sourceArtifactLimitBytes = repository.Limits.SourceArtifactLimitBytes;
        _artifactStorageQuotaBytes = repository.Limits.ArtifactStorageQuotaBytes;
    }

    /// <summary>
    /// Gets the traversal-safe artifact paths.
    /// </summary>
    public SourceArtifactPaths Paths => _paths;

    /// <summary>
    /// Attempts to promote exact source bytes into the immutable store.
    /// </summary>
    /// <param name="bytes">The exact source bytes.</param>
    /// <param name="contentType">The declared source MIME type.</param>
    /// <param name="expectedSha256">The optional declared SHA-256 that must match the bytes.</param>
    /// <returns>The bounded promotion result.</returns>
    public SourceArtifactPromotionResult Promote(
        ReadOnlySpan<byte> bytes,
        string contentType,
        string? expectedSha256 = null)
    {
        if (bytes.Length == 0)
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.InvalidContent,
                "A source artifact requires at least one byte.");
        }

        if (!SourceArtifactFormat.TryNormalizeContentType(contentType, out var normalizedContentType))
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.UnsupportedFormat,
                "The source MIME type is not a supported image format.");
        }

        if (bytes.Length > _sourceArtifactLimitBytes)
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.TooLarge,
                "The source exceeds the configured source-artifact byte limit.");
        }

        if (expectedSha256 is not null && !ArtworkHashes.IsSha256Hex(expectedSha256))
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.HashMismatch,
                "The declared source hash is not a SHA-256 value.");
        }

        var sha256 = ArtworkHashes.ComputeSha256(bytes);
        if (expectedSha256 is not null && !string.Equals(sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.HashMismatch,
                "The declared source hash does not match the supplied bytes.");
        }

        var detectedContentType = SourceArtifactFormat.DetectContentType(bytes);
        if (detectedContentType is not null
            && !string.Equals(detectedContentType, normalizedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.UnsupportedFormat,
                "The declared source MIME type does not match the file signature.");
        }

        var existing = Read(sha256);
        if (existing.Status == SourceArtifactReadStatus.Found && existing.Info is not null)
        {
            return SourceArtifactPromotionResult.Promoted(existing.Info);
        }

        var path = _paths.GetArtifactPath(sha256);
        var existingBytes = GetExistingLength(path);
        if (GetTotalArtifactBytes() - existingBytes + bytes.Length > _artifactStorageQuotaBytes)
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.StorageQuotaExceeded,
                "The authoritative artifact storage quota would be exceeded; new artwork work is rejected.");
        }

        try
        {
            AtomicFileWriter.Write(path, bytes);
        }
        catch (IOException)
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.InvalidContent,
                "The source artifact could not be written.");
        }
        catch (UnauthorizedAccessException)
        {
            return SourceArtifactPromotionResult.Failed(
                SourceArtifactPromotionFailure.InvalidContent,
                "The source artifact could not be written.");
        }

        var info = new SourceArtifactInfo(sha256, normalizedContentType, bytes.Length, sha256);
        _repository.Write(StateAuthority.Authoritative, ManifestKind, sha256, info);
        return SourceArtifactPromotionResult.Promoted(info);
    }

    /// <summary>
    /// Reads and integrity-validates a retained source artifact.
    /// </summary>
    /// <param name="artifactId">The content-addressed artifact identifier.</param>
    /// <returns>The bounded read result.</returns>
    public SourceArtifactReadResult Read(string artifactId)
    {
        if (!SourceArtifactPaths.IsValidArtifactId(artifactId))
        {
            return SourceArtifactReadResult.Invalid("The source artifact identifier is not a SHA-256 value.");
        }

        var normalized = SourceArtifactPaths.NormalizeArtifactId(artifactId);
        var manifest = _repository.Read<SourceArtifactInfo>(StateAuthority.Authoritative, ManifestKind, normalized);
        switch (manifest.Status)
        {
            case StateReadStatus.Missing:
                return SourceArtifactReadResult.Missing();
            case StateReadStatus.InvalidDiscarded:
                return SourceArtifactReadResult.Corrupt("The source artifact manifest was discarded.");
            case StateReadStatus.InvalidQuarantined:
                return SourceArtifactReadResult.Corrupt("The source artifact manifest failed integrity validation.");
        }

        var info = manifest.Value;
        if (info is null)
        {
            return SourceArtifactReadResult.Corrupt("The source artifact manifest is empty.");
        }

        if (!info.Validate(out var reason))
        {
            return SourceArtifactReadResult.Corrupt(reason);
        }

        if (!string.Equals(info.ArtifactId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return SourceArtifactReadResult.Corrupt("The source artifact manifest does not match its identifier.");
        }

        var path = _paths.GetArtifactPath(normalized);
        if (!File.Exists(path))
        {
            return SourceArtifactReadResult.Corrupt("The source artifact bytes are missing.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return SourceArtifactReadResult.Corrupt("The source artifact bytes could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            return SourceArtifactReadResult.Corrupt("The source artifact bytes could not be read.");
        }

        if (bytes.LongLength != info.ByteLength)
        {
            return SourceArtifactReadResult.Corrupt("The source artifact byte length does not match its manifest.");
        }

        var sha256 = ArtworkHashes.ComputeSha256(bytes);
        if (!string.Equals(sha256, info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return SourceArtifactReadResult.Corrupt("The source artifact failed its integrity check.");
        }

        return SourceArtifactReadResult.Found(info, bytes);
    }

    /// <summary>
    /// Determines whether a valid retained source artifact exists.
    /// </summary>
    /// <param name="artifactId">The content-addressed artifact identifier.</param>
    /// <returns><see langword="true"/> when a valid artifact exists.</returns>
    public bool Exists(string artifactId)
    {
        return Read(artifactId).Status == SourceArtifactReadStatus.Found;
    }

    /// <summary>
    /// Enumerates the content-addressed artifact identifiers currently held by
    /// the store, from the persisted manifests and the artifact bytes. The
    /// enumeration is bounded and deterministic. It performs no deletion and no
    /// eviction decision; the artifact retention policy decides what is safe to
    /// remove.
    /// </summary>
    /// <param name="maxRecords">The bounded maximum number of identifiers.</param>
    /// <returns>The distinct artifact identifiers in a bounded deterministic order.</returns>
    public IReadOnlyList<string> EnumerateArtifactIds(int maxRecords = StateRepository.MaxEnumerationRecords)
    {
        if (maxRecords <= 0)
        {
            return Array.Empty<string>();
        }

        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var artifactDirectory = _paths.GetArtifactDirectory();
        if (Directory.Exists(artifactDirectory))
        {
            try
            {
                foreach (var file in new DirectoryInfo(artifactDirectory).EnumerateFiles("*.bin", SearchOption.AllDirectories))
                {
                    var id = Path.GetFileNameWithoutExtension(file.Name);
                    if (SourceArtifactPaths.IsValidArtifactId(id))
                    {
                        ids.Add(SourceArtifactPaths.NormalizeArtifactId(id));
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var manifestDirectory = _repository.Paths.GetKindDirectory(StateAuthority.Authoritative, ManifestKind);
        if (Directory.Exists(manifestDirectory))
        {
            try
            {
                foreach (var file in new DirectoryInfo(manifestDirectory).EnumerateFiles("*.json", SearchOption.TopDirectoryOnly))
                {
                    var id = Path.GetFileNameWithoutExtension(file.Name);
                    if (SourceArtifactPaths.IsValidArtifactId(id))
                    {
                        ids.Add(SourceArtifactPaths.NormalizeArtifactId(id));
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var results = new List<string>(Math.Min(ids.Count, maxRecords));
        foreach (var id in ids)
        {
            if (results.Count >= maxRecords)
            {
                break;
            }

            results.Add(id);
        }

        return results;
    }

    /// <summary>
    /// Gets the newest write time of an artifact's bytes or manifest. It is used
    /// by retention to give a just-promoted artifact a bounded grace period
    /// before it can be reclaimed, closing the window in which a publication has
    /// written an artifact but has not yet made a durable reference to it.
    /// </summary>
    /// <param name="artifactId">The content-addressed artifact identifier.</param>
    /// <param name="lastWriteTime">The newest write time when the artifact exists.</param>
    /// <returns><see langword="true"/> when a byte or manifest file exists.</returns>
    public bool TryGetArtifactLastWriteTime(string artifactId, out DateTimeOffset lastWriteTime)
    {
        lastWriteTime = default;
        if (!SourceArtifactPaths.IsValidArtifactId(artifactId))
        {
            return false;
        }

        var normalized = SourceArtifactPaths.NormalizeArtifactId(artifactId);
        var found = false;
        var newest = DateTimeOffset.MinValue;

        try
        {
            var bytes = new FileInfo(_paths.GetArtifactPath(normalized));
            if (bytes.Exists)
            {
                newest = bytes.LastWriteTimeUtc;
                found = true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            var manifest = new FileInfo(_repository.Paths.GetRecordPath(StateAuthority.Authoritative, ManifestKind, normalized));
            if (manifest.Exists)
            {
                var modified = new DateTimeOffset(manifest.LastWriteTimeUtc, TimeSpan.Zero);
                if (!found || modified > newest)
                {
                    newest = modified;
                }

                found = true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (found)
        {
            lastWriteTime = newest;
        }

        return found;
    }

    /// <summary>
    /// Deletes one artifact's bytes and its authoritative manifest. This is an
    /// explicit retention action: the caller must have proven the artifact is not
    /// the active image, not the retained session source, and not referenced by a
    /// non-terminal operation. A missing file is not an error.
    /// </summary>
    /// <param name="artifactId">The content-addressed artifact identifier.</param>
    /// <returns><see langword="true"/> when a byte or manifest file was removed.</returns>
    public bool Delete(string artifactId)
    {
        if (!SourceArtifactPaths.IsValidArtifactId(artifactId))
        {
            return false;
        }

        var normalized = SourceArtifactPaths.NormalizeArtifactId(artifactId);
        var deleted = false;

        try
        {
            var path = _paths.GetArtifactPath(normalized);
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted = true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            var manifest = _repository.Paths.GetRecordPath(StateAuthority.Authoritative, ManifestKind, normalized);
            if (File.Exists(manifest))
            {
                File.Delete(manifest);
                deleted = true;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return deleted;
    }

    /// <summary>
    /// Gets the total bytes currently held by the artifact store.
    /// </summary>
    /// <returns>The total artifact byte count.</returns>
    public long GetTotalArtifactBytes()
    {
        var directory = _paths.GetArtifactDirectory();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var total = 0L;
        try
        {
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.bin", SearchOption.AllDirectories))
            {
                total += file.Length;
            }
        }
        catch (IOException)
        {
            return total;
        }
        catch (UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }

    private static long GetExistingLength(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0L;
        }
        catch (IOException)
        {
            return 0L;
        }
        catch (UnauthorizedAccessException)
        {
            return 0L;
        }
    }
}
