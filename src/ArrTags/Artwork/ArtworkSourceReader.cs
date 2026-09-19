using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The host-neutral source adapter core. It observes the current active image
/// through the injectable <see cref="IArtworkImageAccess"/> boundary, confines
/// the accepted containers to the PNG and JPEG profiles the renderer inspects,
/// enforces the configured source byte and decoded dimension limits, derives
/// the post-orientation display dimensions, and returns a bounded result that
/// can build both the renderer's <see cref="SourceImageInput"/> and the task 5.2
/// <see cref="ActiveImageIdentity"/>. It performs no Jellyfin access itself, so
/// it is fully testable without a live host.
/// </summary>
public sealed class ArtworkSourceReader : IArtworkSourceReader
{
    private readonly IArtworkImageAccess _access;
    private readonly long _sourceByteLimit;
    private readonly int _maxDimensionPixels;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkSourceReader"/> class.
    /// </summary>
    /// <param name="access">The injectable Jellyfin image-access boundary.</param>
    /// <param name="limits">The accepted operational limits; the source byte and dimension limits are enforced on every read.</param>
    /// <exception cref="ArgumentNullException">The access or limits are <see langword="null"/>.</exception>
    public ArtworkSourceReader(IArtworkImageAccess access, OperationalLimits limits)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        ArgumentNullException.ThrowIfNull(limits);
        _sourceByteLimit = limits.SourceArtifactLimitBytes;
        _maxDimensionPixels = limits.MaxImageDimensionPixels;
    }

    /// <inheritdoc />
    public async Task<ArtworkSourceReadResult> ReadAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsV1Surface(surface))
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.UnsupportedSurface,
                "Only the unindexed Primary image surface is supported.");
        }

        if (itemId == Guid.Empty)
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.ItemNotFound,
                "A non-empty Jellyfin item identifier is required.");
        }

        ArtworkImageAccessResult observation;
        try
        {
            observation = await _access.AccessAsync(itemId, surface, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The active image could not be observed through the host boundary.");
        }

        if (observation is null)
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The host boundary returned no image observation.");
        }

        return observation.Status switch
        {
            ArtworkImageAccessStatus.Absent => ArtworkSourceReadResult.Absent(surface),
            ArtworkImageAccessStatus.Present => ProcessPresent(surface, observation),
            _ => MapFailure(surface, observation.Failure),
        };
    }

    private ArtworkSourceReadResult ProcessPresent(
        ArtworkImageSurface surface,
        ArtworkImageAccessResult observation)
    {
        var bytes = observation.Bytes;
        if (bytes.IsEmpty)
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The active image representation was empty.");
        }

        if (bytes.Length > _sourceByteLimit)
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.SourceTooLarge,
                "The source exceeds the configured source-artifact byte limit.");
        }

        // V1 confinement: only the containers the renderer's color-profile
        // inspection understands are accepted, so a malformed profile in an
        // uninspected container can never be silently treated as sRGB.
        if (!ArtworkSourceContentType.TryDetect(bytes.Span, out var contentType))
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.UnsupportedContentType,
                "Only PNG and JPEG source containers are supported.");
        }

        if (observation.EncodedWidth <= 0
            || observation.EncodedHeight <= 0
            || !Enum.IsDefined(observation.Orientation))
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The source image dimensions could not be determined.");
        }

        var (orientedWidth, orientedHeight) = observation.Orientation.OrientedDimensions(
            observation.EncodedWidth,
            observation.EncodedHeight);

        if (orientedWidth > _maxDimensionPixels || orientedHeight > _maxDimensionPixels)
        {
            return ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.DimensionTooLarge,
                "The source exceeds the configured decoded dimension limit.");
        }

        var contentSha256 = ArtworkHashes.ComputeSha256(bytes.Span);
        return ArtworkSourceReadResult.Present(
            surface,
            contentType,
            bytes,
            contentSha256,
            orientedWidth,
            orientedHeight,
            observation.DateModifiedUtc,
            observation.JellyfinImageTag);
    }

    private static ArtworkSourceReadResult MapFailure(
        ArtworkImageSurface surface,
        ArtworkImageAccessFailure failure)
    {
        return failure switch
        {
            ArtworkImageAccessFailure.ItemNotFound => ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.ItemNotFound,
                "The Jellyfin item was not found."),
            ArtworkImageAccessFailure.UnsupportedSurface => ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.UnsupportedSurface,
                "Only the unindexed Primary image surface is supported."),
            ArtworkImageAccessFailure.UnsupportedContentType => ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.UnsupportedContentType,
                "Only PNG and JPEG source containers are supported."),
            ArtworkImageAccessFailure.SourceTooLarge => ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.SourceTooLarge,
                "The source exceeds the configured source-artifact byte limit."),
            _ => ArtworkSourceReadResult.Failed(
                surface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The active image could not be read from Jellyfin."),
        };
    }

    private static bool IsV1Surface(ArtworkImageSurface surface)
    {
        return surface.ImageType == ArtworkImageType.Primary && surface.Index is null;
    }
}
