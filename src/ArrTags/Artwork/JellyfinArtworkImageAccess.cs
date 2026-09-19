using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Rendering;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace ArrTags.Artwork;

/// <summary>
/// The Jellyfin 12.0.0 host implementation of the source-image access boundary.
/// It resolves a non-empty item through <see cref="ILibraryManager"/>, reads the
/// unindexed <c>Primary</c> image information, converts a non-local image to a
/// local file through the supported <c>ConvertImageToLocal</c> API when
/// required, reads a bounded byte copy, and observes the Jellyfin image tag and
/// modification time. The post-orientation dimensions are derived from the exact
/// bytes with the pinned raster stack because Jellyfin reports the
/// pre-orientation encoded dimensions.
/// </summary>
/// <remarks>
/// Every failure is mapped to a bounded no-source result; no Jellyfin exception
/// escapes this boundary. All Jellyfin references are confined to this file. The
/// returned observation carries no path or Jellyfin entity to the renderer.
/// </remarks>
public sealed class JellyfinArtworkImageAccess : IArtworkImageAccess
{
    private const int V1ImageIndex = 0;
    private const ImageType V1ImageType = ImageType.Primary;

    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly long _sourceByteLimit;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinArtworkImageAccess"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager used for read-only item and image lookups.</param>
    /// <param name="imageProcessor">The Jellyfin image processor used for the supporting image tag.</param>
    /// <param name="limits">The accepted operational limits; the source byte limit bounds the read.</param>
    /// <exception cref="ArgumentNullException">A dependency or the limits are <see langword="null"/>.</exception>
    public JellyfinArtworkImageAccess(
        ILibraryManager libraryManager,
        IImageProcessor imageProcessor,
        OperationalLimits limits)
    {
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        ArgumentNullException.ThrowIfNull(limits);
        _sourceByteLimit = limits.SourceArtifactLimitBytes;
    }

    /// <inheritdoc />
    public async Task<ArtworkImageAccessResult> AccessAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        cancellationToken.ThrowIfCancellationRequested();

        if (itemId == Guid.Empty)
        {
            return ArtworkImageAccessResult.Failed(
                ArtworkImageAccessFailure.ItemNotFound,
                "A non-empty Jellyfin item identifier is required.");
        }

        if (surface.ImageType != ArtworkImageType.Primary || surface.Index is not null)
        {
            return ArtworkImageAccessResult.Failed(
                ArtworkImageAccessFailure.UnsupportedSurface,
                "Only the unindexed Primary image surface is supported.");
        }

        try
        {
            return await AccessCoreAsync(itemId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ArtworkImageAccessResult.Failed(
                ArtworkImageAccessFailure.Unreadable,
                "The active image could not be read through Jellyfin.");
        }
    }

    private async Task<ArtworkImageAccessResult> AccessCoreAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return ArtworkImageAccessResult.Failed(
                ArtworkImageAccessFailure.ItemNotFound,
                "The Jellyfin item was not found.");
        }

        var info = item.GetImageInfo(V1ImageType, V1ImageIndex);
        if (info is null)
        {
            return ArtworkImageAccessResult.Absent();
        }

        if (!info.IsLocalFile)
        {
            // The ImageController uses the same supported conversion. It is only
            // needed when the active representation is not already a local file;
            // removeOnFailure is false so a read never removes an image.
            info = await _libraryManager
                .ConvertImageToLocal(item, info, V1ImageIndex, removeOnFailure: false)
                .ConfigureAwait(false);

            if (info is null)
            {
                return ArtworkImageAccessResult.Failed(
                    ArtworkImageAccessFailure.Unreadable,
                    "The non-local image could not be converted to a local file.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadBounded(info.Path, out var bytes, out var tooLarge))
        {
            return ArtworkImageAccessResult.Failed(
                tooLarge ? ArtworkImageAccessFailure.SourceTooLarge : ArtworkImageAccessFailure.Unreadable,
                tooLarge
                    ? "The source exceeds the configured source-artifact byte limit."
                    : "The active image bytes could not be read.");
        }

        // Confine before handing anything to the raster stack so an uninspected
        // container is never decoded.
        if (!ArtworkSourceContentType.TryDetect(bytes, out _))
        {
            return ArtworkImageAccessResult.Failed(
                ArtworkImageAccessFailure.UnsupportedContentType,
                "Only PNG and JPEG source containers are supported.");
        }

        if (!SourceImageDescriptor.TryRead(bytes, out var width, out var height, out var orientation))
        {
            return ArtworkImageAccessResult.Failed(
                ArtworkImageAccessFailure.Unreadable,
                "The source image header could not be understood.");
        }

        var tag = TryGetImageTag(item, info);
        return ArtworkImageAccessResult.Present(
            bytes,
            width,
            height,
            orientation,
            ToUtc(info.DateModified),
            tag);
    }

    private bool TryReadBounded(string? path, out byte[] bytes, out bool tooLarge)
    {
        bytes = [];
        tooLarge = false;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length > _sourceByteLimit)
            {
                tooLarge = true;
                return false;
            }

            if (stream.Length <= 0)
            {
                return false;
            }

            var buffer = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset != buffer.Length)
            {
                return false;
            }

            bytes = buffer;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private string? TryGetImageTag(BaseItem item, ItemImageInfo info)
    {
        try
        {
            var tag = _imageProcessor.GetImageCacheTag(item, info);
            return string.IsNullOrWhiteSpace(tag) || tag.Length > 512 ? null : tag;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DateTimeOffset? ToUtc(DateTime value)
    {
        if (value == default)
        {
            return null;
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value),
            DateTimeKind.Local => new DateTimeOffset(value),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
        };
    }
}
