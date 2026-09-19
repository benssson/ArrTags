using System;
using System.Text;
using ArrTags.Artwork;

namespace ArrTags.Tests;

/// <summary>
/// Shared factory helpers for the artwork provenance tests. They build canonical
/// identities and valid states through the real transitions so the tests
/// exercise the same invariants production code relies on.
/// </summary>
internal static class ArtworkStateFixtures
{
    /// <summary>
    /// Creates a present active-image identity for the supplied content marker.
    /// </summary>
    /// <param name="surface">The image surface.</param>
    /// <param name="content">The content marker whose bytes define the hash.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="tag">The Jellyfin image tag.</param>
    /// <param name="modified">The modification time.</param>
    /// <returns>A present identity.</returns>
    public static ActiveImageIdentity Present(
        ArtworkImageSurface surface,
        string content,
        int width = 100,
        int height = 150,
        string? tag = "tag-1",
        DateTimeOffset? modified = null)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new ActiveImageIdentity(
            surface,
            ArtworkImagePresence.Present,
            ArtworkHashes.ComputeSha256(bytes),
            bytes.Length,
            width,
            height,
            modified ?? DateTimeOffset.UnixEpoch,
            tag);
    }

    /// <summary>
    /// Creates content-addressed artifact metadata for the supplied bytes.
    /// </summary>
    /// <param name="bytes">The artifact bytes.</param>
    /// <param name="contentType">The artifact MIME type.</param>
    /// <returns>The artifact metadata.</returns>
    public static SourceArtifactInfo Artifact(byte[] bytes, string contentType = "image/png")
    {
        var sha = ArtworkHashes.ComputeSha256(bytes);
        return new SourceArtifactInfo(sha, contentType, bytes.Length, sha);
    }

    /// <summary>
    /// Captures a session and commits a first publication.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface.</param>
    /// <param name="sourceContent">The source content marker.</param>
    /// <param name="activeContent">The active content marker.</param>
    /// <param name="updatedAt">The commit time.</param>
    /// <returns>A valid published state.</returns>
    public static PublishedArtworkState Published(
        Guid jellyfinItemId,
        ArtworkImageSurface surface,
        string sourceContent = "source-bytes",
        string activeContent = "active-bytes",
        DateTimeOffset? updatedAt = null)
    {
        var at = updatedAt ?? DateTimeOffset.UnixEpoch.AddDays(1);
        var capture = Present(surface, sourceContent);
        var artifact = Artifact(Encoding.UTF8.GetBytes(sourceContent));
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, jellyfinItemId, surface, capture, artifact, at)
            .State;
        var active = Present(surface, activeContent, tag: "active-tag");

        return PublishedArtworkStateTransitions
            .CommitPublication(session, active, ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes(activeContent + "-fingerprint")), 2, at)
            .State;
    }
}
