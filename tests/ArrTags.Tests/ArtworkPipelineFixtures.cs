using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Metadata;
using ArrTags.Rendering;

namespace ArrTags.Tests;

/// <summary>
/// Shared doubles for the task 6.6 artwork publication pipeline tests. The host
/// source/image boundary and the renderer are injected so the pipeline runs
/// without a live Jellyfin host and without the native Skia runtime. The renderer
/// reproduces the production output-fingerprint computation exactly, so the
/// publication-fingerprint gate can be exercised deterministically.
/// </summary>
internal sealed class PipelineArtworkHost : IArtworkSourceReader, IArtworkImageWriter
{
    /// <summary>
    /// The PNG signature a rendered result must carry.
    /// </summary>
    public static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>
    /// Gets or sets the current active surface bytes, or <see langword="null"/> for an absent surface.
    /// </summary>
    public byte[]? CurrentBytes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a save replaces the active surface bytes.
    /// </summary>
    public bool SaveAppliesBytes { get; set; } = true;

    /// <summary>
    /// Gets the number of source reads.
    /// </summary>
    public int ReadCalls { get; private set; }

    /// <summary>
    /// Gets the number of image saves.
    /// </summary>
    public int SaveCalls { get; private set; }

    /// <summary>
    /// Gets the number of item-update persists.
    /// </summary>
    public int UpdateCalls { get; private set; }

    /// <summary>
    /// Gets the exact bytes written by the last save.
    /// </summary>
    public byte[]? SavedBytes { get; private set; }

    /// <summary>
    /// Gets the byte length of the active surface when present.
    /// </summary>
    public int CurrentLength => CurrentBytes?.Length ?? 0;

    /// <inheritdoc />
    public Task<ArtworkSourceReadResult> ReadAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCalls++;
        return Task.FromResult(CurrentBytes is null
            ? ArtworkSourceReadResult.Absent(surface)
            : ArtworkSourceReadResult.Present(
                surface,
                "image/png",
                CurrentBytes,
                ArtworkHashes.ComputeSha256(CurrentBytes),
                100,
                150,
                DateTimeOffset.UnixEpoch,
                "host-tag"));
    }

    /// <inheritdoc />
    public Task<ArtworkImageMutationResult> SaveImageAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCalls++;
        SavedBytes = content.ToArray();
        if (SaveAppliesBytes)
        {
            CurrentBytes = SavedBytes;
        }

        return Task.FromResult(ArtworkImageMutationResult.Success());
    }

    /// <inheritdoc />
    public Task<ArtworkImageMutationResult> PersistItemUpdateAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateCalls++;
        return Task.FromResult(ArtworkImageMutationResult.Success());
    }

    /// <inheritdoc />
    public Task<ArtworkImageMutationResult> RemoveImageAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentBytes = null;
        return Task.FromResult(ArtworkImageMutationResult.Success());
    }
}

/// <summary>
/// A deterministic in-memory renderer that derives one PNG artifact from the
/// exact source bytes and the resolved selection, and computes the production
/// output fingerprint with <see cref="RenderFingerprint"/>. Deriving the bytes
/// from the source hash makes a stacked (double-badged) repeat publication
/// observable.
/// </summary>
internal sealed class FingerprintingRenderer : IRenderer
{
    /// <summary>
    /// Gets the number of render calls.
    /// </summary>
    public int Calls { get; private set; }

    /// <summary>
    /// Gets the last render request.
    /// </summary>
    public RenderRequest? LastRequest { get; private set; }

    /// <inheritdoc />
    public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        LastRequest = request;

        var identity = request.MediaIdentity;
        if (identity.ItemType is not (ArrTags.Media.MediaItemType.Movie or ArrTags.Media.MediaItemType.Episode))
        {
            return Task.FromResult(RenderResult.PassThrough(RenderPassThroughReason.IneligibleSurface));
        }

        if (request.Match.Status != ArrTags.Matching.MediaMatchStatus.Matched)
        {
            return Task.FromResult(RenderResult.PassThrough(RenderPassThroughReason.MatchNotEligible));
        }

        if (request.Metadata is null)
        {
            return Task.FromResult(RenderResult.PassThrough(RenderPassThroughReason.NoMetadata));
        }

        var selection = BadgeDefinitionResolver.Resolve(request.Metadata, request.BadgeDefinitions);
        if (selection.IsEmpty)
        {
            return Task.FromResult(RenderResult.PassThrough(RenderPassThroughReason.NoDisplayableValue));
        }

        var source = request.SourceImage;
        if (source is null)
        {
            return Task.FromResult(RenderResult.PassThrough(RenderPassThroughReason.SourceUnavailable));
        }

        var bytes = DerivedBytes(source, request.Metadata);
        var input = new RenderFingerprintInput(
            identity.JellyfinItemId,
            identity.ItemType,
            source.SourceSha256,
            source.OrientedWidth,
            source.OrientedHeight,
            request.Metadata.MetadataFingerprint,
            request.ConfigurationFingerprint,
            selection,
            request.OutputPolicy,
            request.RendererVersion,
            request.BadgeSchemaVersion);

        return Task.FromResult(RenderResult.Rendered(
            bytes,
            source.OrientedWidth,
            source.OrientedHeight,
            ArtworkHashes.ComputeSha256(bytes),
            RenderFingerprint.ComputeOutputFingerprint(input)));
    }

    /// <summary>
    /// Derives deterministic PNG bytes that encode the source identity and the
    /// metadata fingerprint, so a repeat publication that rendered from a derived
    /// image instead of the retained original is detectable.
    /// </summary>
    /// <param name="source">The render source descriptor.</param>
    /// <param name="metadata">The canonical metadata.</param>
    /// <returns>A complete PNG byte sequence.</returns>
    public static byte[] DerivedBytes(SourceImageInput source, BadgeMetadata metadata)
    {
        var marker = Encoding.UTF8.GetBytes(
            string.Concat("derived|", source.SourceSha256, "|", metadata.MetadataFingerprint));
        var bytes = new byte[PipelineArtworkHost.PngSignature.Length + marker.Length];
        PipelineArtworkHost.PngSignature.CopyTo(bytes, 0);
        marker.CopyTo(bytes, PipelineArtworkHost.PngSignature.Length);
        return bytes;
    }
}
