using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace ArrTags.Artwork;

/// <summary>
/// The Jellyfin 12.0.0 host implementation of the image-mutation boundary. It
/// resolves the target item through <see cref="ILibraryManager"/> and uses the
/// supported <see cref="IProviderManager"/> stream <c>SaveImage</c> overload
/// followed by <see cref="BaseItem.UpdateToRepositoryAsync"/> with
/// <see cref="ItemUpdateType.ImageUpdate"/>, which is the same supported flow
/// the standard item-image controller uses. It never writes a media-folder
/// poster, Jellyfin's image cache, or an item-image path directly, never uses
/// the filesystem-path overload that deletes its source, and never publishes
/// derived bytes through a URL overload.
/// </summary>
/// <remarks>
/// Every failure is mapped to a bounded result; only cancellation is
/// rethrown so the caller can finish with an uncertainty-safe outcome. All
/// Jellyfin references for publication are confined to this file.
/// </remarks>
public sealed class JellyfinArtworkImageWriter : IArtworkImageWriter
{
    private const ImageType V1ImageType = ImageType.Primary;

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinArtworkImageWriter"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager used for item resolution.</param>
    /// <param name="providerManager">The Jellyfin provider manager that owns the supported image mutation.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public JellyfinArtworkImageWriter(ILibraryManager libraryManager, IProviderManager providerManager)
    {
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        _providerManager = providerManager ?? throw new ArgumentNullException(nameof(providerManager));
    }

    /// <inheritdoc />
    public async Task<ArtworkImageMutationResult> SaveImageAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        cancellationToken.ThrowIfCancellationRequested();

        if (itemId == Guid.Empty)
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.ItemNotFound,
                "A non-empty Jellyfin item identifier is required.");
        }

        if (surface.ImageType != ArtworkImageType.Primary || surface.Index is not null)
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.UnsupportedSurface,
                "Only the unindexed Primary image surface is supported.");
        }

        if (content.IsEmpty || !ArtworkSourceContentType.IsConfined(contentType))
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.InvalidContent,
                "Only a non-empty confined PNG or JPEG derivative can be published.");
        }

        try
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item is null)
            {
                return ArtworkImageMutationResult.Failure(
                    ArtworkImageMutationStatus.ItemNotFound,
                    "The Jellyfin item was not found.");
            }

            // The stream overload is the only supported publication surface that
            // takes plugin-owned bytes without deleting a source path. The
            // provider owns and disposes the stream.
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            await _providerManager
                .SaveImage(item, stream, contentType, V1ImageType, null, cancellationToken)
                .ConfigureAwait(false);

            return ArtworkImageMutationResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.Failed,
                "The active image could not be published through Jellyfin.");
        }
    }

    /// <inheritdoc />
    public async Task<ArtworkImageMutationResult> PersistItemUpdateAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        cancellationToken.ThrowIfCancellationRequested();

        if (itemId == Guid.Empty)
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.ItemNotFound,
                "A non-empty Jellyfin item identifier is required.");
        }

        if (surface.ImageType != ArtworkImageType.Primary || surface.Index is not null)
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.UnsupportedSurface,
                "Only the unindexed Primary image surface is supported.");
        }

        try
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item is null)
            {
                return ArtworkImageMutationResult.Failure(
                    ArtworkImageMutationStatus.ItemNotFound,
                    "The Jellyfin item was not found.");
            }

            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
            return ArtworkImageMutationResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ArtworkImageMutationResult.Failure(
                ArtworkImageMutationStatus.Failed,
                "The item image update could not be persisted through Jellyfin.");
        }
    }
}
