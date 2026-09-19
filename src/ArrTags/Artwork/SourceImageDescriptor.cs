using System;
using System.IO;
using ArrTags.Rendering;
using SkiaSharp;

namespace ArrTags.Artwork;

/// <summary>
/// Reads the encoded pixel dimensions and EXIF orientation of a bounded source
/// image header with the pinned SkiaSharp stack that the renderer itself decodes
/// with. Jellyfin's dimension surface returns the pre-orientation encoded
/// dimensions, so the host source adapter derives the post-orientation display
/// dimensions from the exact bytes instead of trusting a raw or cached
/// dimension. The reader never emits an image that this type cannot describe;
/// undecodable bytes are a bounded no-source failure.
/// </summary>
/// <remarks>
/// This type reads only the header. It never decodes pixels, mutates the input,
/// or exposes a path, image object, or SkiaSharp type outside the adapter.
/// </remarks>
public static class SourceImageDescriptor
{
    /// <summary>
    /// Reads the encoded dimensions and orientation of a source image header.
    /// </summary>
    /// <param name="bytes">The exact bounded source bytes.</param>
    /// <param name="encodedWidth">The pre-orientation encoded width in pixels.</param>
    /// <param name="encodedHeight">The pre-orientation encoded height in pixels.</param>
    /// <param name="orientation">The provider-neutral EXIF orientation.</param>
    /// <returns><see langword="true"/> when the header was understood.</returns>
    public static bool TryRead(
        ReadOnlySpan<byte> bytes,
        out int encodedWidth,
        out int encodedHeight,
        out SourceOrientation orientation)
    {
        encodedWidth = 0;
        encodedHeight = 0;
        orientation = SourceOrientation.TopLeft;

        if (bytes.IsEmpty)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                return false;
            }

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            encodedWidth = width;
            encodedHeight = height;
            orientation = SkiaOrientation.FromEncodedOrigin(codec.EncodedOrigin);
            return true;
        }
        catch (Exception)
        {
            // A malformed or unsupported header is a bounded no-source result,
            // never a thrown exception into a Jellyfin request.
            return false;
        }
    }
}
