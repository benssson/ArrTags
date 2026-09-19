using System;
using System.Text;
using ArrTags.Artwork;

namespace ArrTags.Tests;

/// <summary>
/// Factory helpers for the durable artwork-operation tests. They build valid
/// operations through the real model so the tests exercise the same invariants
/// production code relies on, and they can copy an operation with an overridden
/// generation, phase, or identifier for the generation-fence checks.
/// </summary>
internal static class ArtworkOperationFixtures
{
    /// <summary>
    /// The default Jellyfin item used by the operation tests.
    /// </summary>
    public static readonly Guid Item = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    /// <summary>
    /// The default image surface used by the operation tests.
    /// </summary>
    public static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    /// <summary>
    /// The default journal time used by the operation tests.
    /// </summary>
    public static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(5);

    /// <summary>
    /// Creates a valid publication operation.
    /// </summary>
    /// <param name="item">The Jellyfin item; defaults to the shared item.</param>
    /// <param name="surface">The image surface; defaults to the shared surface.</param>
    /// <param name="generation">The generation.</param>
    /// <param name="phase">The phase.</param>
    /// <param name="operationId">An explicit operation identifier, or a new random one.</param>
    /// <param name="ownershipToken">An explicit ownership token, or a new random one.</param>
    /// <param name="sourcePresence">The source baseline presence.</param>
    /// <param name="lifecycleFence">The lifecycle fence.</param>
    /// <param name="updatedAt">The journal time.</param>
    /// <param name="lastError">An optional diagnostic summary.</param>
    /// <returns>A valid publication operation.</returns>
    public static ArtworkOperation Publication(
        Guid? item = null,
        ArtworkImageSurface? surface = null,
        long generation = 1,
        ArtworkOperationPhase phase = ArtworkOperationPhase.Prepared,
        string? operationId = null,
        string? ownershipToken = null,
        ArtworkImagePresence sourcePresence = ArtworkImagePresence.Present,
        ArtworkLifecycleFence lifecycleFence = ArtworkLifecycleFence.Normal,
        DateTimeOffset? updatedAt = null,
        string? lastError = null)
    {
        var subject = item ?? Item;
        var imageSurface = surface ?? Surface;
        var at = updatedAt ?? At;
        var sourceHash = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("source-bytes"));
        var derivedHash = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("derived-bytes"));

        return new ArtworkOperation(
            operationId ?? ArtworkTokens.Create(),
            ArtworkOperationKind.Publication,
            subject,
            imageSurface,
            generation,
            ArtworkStateFixtures.Present(imageSurface, "before-bytes"),
            sourcePresence,
            ArtworkImagePresence.Present,
            phase,
            lifecycleFence,
            at,
            at,
            ownershipToken: ownershipToken ?? ArtworkTokens.Create(),
            priorPublicationToken: ArtworkTokens.Create(),
            publicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: derivedHash,
            sourceArtifactId: sourcePresence == ArtworkImagePresence.Present ? sourceHash : null,
            derivedArtifactId: ArtworkTokens.Create(),
            lastError: lastError);
    }

    /// <summary>
    /// Creates a valid restoration operation.
    /// </summary>
    /// <param name="item">The Jellyfin item; defaults to the shared item.</param>
    /// <param name="surface">The image surface; defaults to the shared surface.</param>
    /// <param name="generation">The generation.</param>
    /// <param name="phase">The phase.</param>
    /// <param name="operationId">An explicit operation identifier, or a new random one.</param>
    /// <param name="sourcePresence">The retained source baseline presence.</param>
    /// <param name="updatedAt">The journal time.</param>
    /// <returns>A valid restoration operation.</returns>
    public static ArtworkOperation Restoration(
        Guid? item = null,
        ArtworkImageSurface? surface = null,
        long generation = 1,
        ArtworkOperationPhase phase = ArtworkOperationPhase.Prepared,
        string? operationId = null,
        ArtworkImagePresence sourcePresence = ArtworkImagePresence.Present,
        DateTimeOffset? updatedAt = null)
    {
        var subject = item ?? Item;
        var imageSurface = surface ?? Surface;
        var at = updatedAt ?? At;
        var sourceHash = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("source-bytes"));
        var present = sourcePresence == ArtworkImagePresence.Present;

        return new ArtworkOperation(
            operationId ?? ArtworkTokens.Create(),
            ArtworkOperationKind.Restoration,
            subject,
            imageSurface,
            generation,
            ArtworkStateFixtures.Present(imageSurface, "active-bytes"),
            sourcePresence,
            sourcePresence,
            phase,
            ArtworkLifecycleFence.Normal,
            at,
            at,
            ownershipToken: ArtworkTokens.Create(),
            priorPublicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: present ? sourceHash : null,
            sourceArtifactId: present ? sourceHash : null);
    }

    /// <summary>
    /// Creates a terminal item-removal tombstone.
    /// </summary>
    /// <param name="item">The Jellyfin item; defaults to the shared item.</param>
    /// <param name="surface">The image surface; defaults to the shared surface.</param>
    /// <param name="updatedAt">The journal time.</param>
    /// <returns>A terminal tombstone operation.</returns>
    public static ArtworkOperation Tombstone(
        Guid? item = null,
        ArtworkImageSurface? surface = null,
        DateTimeOffset? updatedAt = null)
    {
        return Publication(
            item,
            surface,
            generation: 2,
            phase: ArtworkOperationPhase.Aborted,
            lifecycleFence: ArtworkLifecycleFence.ItemRemoved,
            updatedAt: updatedAt);
    }

    /// <summary>
    /// Copies an operation with an overridden generation, phase, or identifier.
    /// </summary>
    /// <param name="operation">The source operation.</param>
    /// <param name="generation">An overridden generation, or the source generation.</param>
    /// <param name="phase">An overridden phase, or the source phase.</param>
    /// <param name="operationId">An overridden operation identifier, or the source identifier.</param>
    /// <param name="updatedAt">An overridden journal time, or the source time.</param>
    /// <returns>A new operation with the overrides applied.</returns>
    public static ArtworkOperation Copy(
        ArtworkOperation operation,
        long? generation = null,
        ArtworkOperationPhase? phase = null,
        string? operationId = null,
        DateTimeOffset? updatedAt = null)
    {
        return new ArtworkOperation(
            operationId ?? operation.OperationId,
            operation.Kind,
            operation.JellyfinItemId,
            operation.ImageSurface,
            generation ?? operation.Generation,
            operation.ExpectedBeforeIdentity,
            operation.SourcePresence,
            operation.CandidateAfterPresence,
            phase ?? operation.Phase,
            operation.LifecycleFence,
            operation.CreatedAt,
            updatedAt ?? operation.UpdatedAt,
            operation.ModelVersion,
            operation.OwnershipToken,
            operation.PriorPublicationToken,
            operation.PublicationToken,
            operation.CandidateAfterContentSha256,
            operation.ObservedAfterIdentity,
            operation.SourceArtifactId,
            operation.DerivedArtifactId,
            operation.Attempt,
            operation.LastError);
    }
}
