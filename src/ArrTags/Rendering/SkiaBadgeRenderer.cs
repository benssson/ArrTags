using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Matching;
using ArrTags.Media;
using SkiaSharp;

namespace ArrTags.Rendering;

/// <summary>
/// The ADR-010 V1 renderer: a plugin-owned SkiaSharp drawing engine with no
/// Jellyfin, provider, filesystem, or network side effects. It revalidates the
/// bounded request, resolves the provider-neutral selection and templated
/// values, enforces the image limits and contrast policy, decodes the source
/// bytes, applies orientation, packs the rail, draws the pills with the bundled
/// font, and encodes a deterministic non-interlaced 8-bit sRGB PNG with the
/// fixed ADR-010 encoder settings.
/// </summary>
public sealed class SkiaBadgeRenderer : IRenderer
{
    private const int PngZLibLevel = 6;

    private const SKPngEncoderFilterFlags PngFilterFlags = SKPngEncoderFilterFlags.AllFilters;

    /// <inheritdoc />
    public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The drawing engine is bounded synchronous work; the caller owns any
        // scheduling. Cancellation is observed at the documented checkpoints.
        return Task.FromResult(Render(request, cancellationToken));
    }

    private static RenderResult Render(RenderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return RenderCore(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return RenderResult.Failed(RenderFailureReason.Cancelled);
        }
        catch (DllNotFoundException)
        {
            return RenderResult.Failed(RenderFailureReason.NativeAssetUnavailable);
        }
        catch (TypeInitializationException)
        {
            return RenderResult.Failed(RenderFailureReason.NativeAssetUnavailable);
        }
        catch (Exception)
        {
            return RenderResult.Failed(RenderFailureReason.RenderError);
        }
    }

    private static RenderResult RenderCore(RenderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = request.MediaIdentity;
        if (identity.ItemType is not (MediaItemType.Movie or MediaItemType.Episode))
        {
            return RenderResult.PassThrough(RenderPassThroughReason.IneligibleSurface);
        }

        if (request.Match.Status != MediaMatchStatus.Matched)
        {
            return RenderResult.PassThrough(RenderPassThroughReason.MatchNotEligible);
        }

        var metadata = request.Metadata;
        if (metadata is null)
        {
            return RenderResult.PassThrough(RenderPassThroughReason.NoMetadata);
        }

        var selection = BadgeDefinitionResolver.Resolve(metadata, request.BadgeDefinitions);
        if (selection.IsEmpty)
        {
            return RenderResult.PassThrough(RenderPassThroughReason.NoDisplayableValue);
        }

        var source = request.SourceImage;
        if (source is null)
        {
            return RenderResult.PassThrough(RenderPassThroughReason.SourceUnavailable);
        }

        var limits = request.Limits;
        var sourceLimit = RenderLimitGuard.ValidateSourceImage(
            source.Bytes.Length,
            source.OrientedWidth,
            source.OrientedHeight,
            limits);
        if (sourceLimit.IsRejected)
        {
            return RenderResult.Failed(MapLimitReason(sourceLimit.Reason));
        }

        var outputLimit = RenderLimitGuard.ValidateDerivedOutput(
            source.OrientedWidth,
            source.OrientedHeight,
            limits);
        if (outputLimit.IsRejected)
        {
            return RenderResult.Failed(MapLimitReason(outputLimit.Reason));
        }

        // ADR-010: an input without a profile is treated as sRGB, and a
        // supported embedded profile is converted to sRGB by the sRGB decode
        // destination. An invalid or unsupported profile fails closed here
        // rather than being silently guessed as sRGB.
        if (SourceColorProfile.Inspect(source.Bytes.Span) == SourceColorProfileKind.Invalid)
        {
            return RenderResult.Failed(RenderFailureReason.UnsupportedColorProfile);
        }

        var contrast = BadgeContrast.Validate(request.OutputPolicy);
        if (contrast is not null)
        {
            return RenderResult.Failed(contrast.Value);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Draw(request, selection, cancellationToken);
    }

    private static RenderResult Draw(
        RenderRequest request,
        BadgeSelection selection,
        CancellationToken cancellationToken)
    {
        var policy = request.OutputPolicy;
        var source = request.SourceImage!;

        using var fontData = LoadFontData(policy, out var fontFailure);
        if (fontData is null)
        {
            return RenderResult.Failed(fontFailure);
        }

        using var typeface = SKTypeface.FromData(fontData);
        if (typeface is null)
        {
            return RenderResult.Failed(RenderFailureReason.FontInvalid);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var sourceStream = new MemoryStream(source.Bytes.ToArray(), writable: false);
        using var codec = SKCodec.Create(sourceStream);
        if (codec is null)
        {
            return RenderResult.Failed(RenderFailureReason.UnsupportedInput);
        }

        var decodeInfo = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());

        using var decoded = SKBitmap.Decode(codec, decodeInfo);
        if (decoded is null)
        {
            return RenderResult.Failed(RenderFailureReason.DecodeFailed);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orientation = SkiaOrientation.FromEncodedOrigin(codec.EncodedOrigin);
        var (orientedWidth, orientedHeight) = orientation.OrientedDimensions(decoded.Width, decoded.Height);
        if (orientedWidth != source.OrientedWidth || orientedHeight != source.OrientedHeight)
        {
            return RenderResult.Failed(RenderFailureReason.MalformedSource);
        }

        using var transformed = orientation == SourceOrientation.TopLeft
            ? null
            : SkiaOrientation.Apply(decoded, orientation);
        var working = transformed ?? decoded;

        var hasAlpha = HasMeaningfulAlpha(working);
        var outputInfo = hasAlpha
            ? new SKImageInfo(orientedWidth, orientedHeight, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb())
            : new SKImageInfo(orientedWidth, orientedHeight, SKColorType.Rgb888x, SKAlphaType.Opaque, SKColorSpace.CreateSrgb());

        using var output = new SKBitmap(outputInfo);
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(working, 0, 0);

            using var font = new SKFont(typeface, (float)(BadgeGeometry.FontSize * BadgeGeometry.ComputeEffectiveScale(orientedWidth, orientedHeight, policy)));
            using var pillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var textPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            var measure = new Func<string, float>(text => font.MeasureText(text, textPaint));
            var layout = BadgeLayoutEngine.Build(
                selection.TechnicalValues,
                selection.StatusValue,
                orientedWidth,
                orientedHeight,
                policy,
                measure);

            cancellationToken.ThrowIfCancellationRequested();

            if (layout.IsEmpty)
            {
                return RenderResult.PassThrough(RenderPassThroughReason.NoDisplayableValue);
            }

            var scale = layout.Scale;
            var cornerRadius = (float)(BadgeGeometry.CornerRadius * scale);
            var horizontalPadding = (float)(BadgeGeometry.HorizontalPadding * scale);
            var verticalPadding = (float)(BadgeGeometry.VerticalPadding * scale);
            var ascent = font.Metrics.Ascent;

            var technicalBackground = ParseColor(policy.TechnicalBackground);
            var technicalText = ParseColor(policy.TechnicalText);
            foreach (var pill in layout.TechnicalPills)
            {
                DrawPill(
                    canvas,
                    pill,
                    technicalBackground,
                    technicalText,
                    pillPaint,
                    textPaint,
                    font,
                    cornerRadius,
                    horizontalPadding,
                    verticalPadding,
                    ascent);
            }

            if (layout.StatusPill is BadgePillPlacement statusPill)
            {
                DrawPill(
                    canvas,
                    statusPill,
                    ParseColor(policy.StatusBackground),
                    ParseColor(policy.StatusText),
                    pillPaint,
                    textPaint,
                    font,
                    cornerRadius,
                    horizontalPadding,
                    verticalPadding,
                    ascent);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var pngBytes = Encode(output);
        if (pngBytes is null)
        {
            return RenderResult.Failed(RenderFailureReason.EncodeFailed);
        }

        if (pngBytes.Length > request.Limits.DerivedArtifactLimitBytes)
        {
            return RenderResult.Failed(RenderFailureReason.OutputByteLimitExceeded);
        }

        var outputHash = Convert.ToHexString(SHA256.HashData(pngBytes));
        var fingerprint = new RenderFingerprintInput(
            request.MediaIdentity.JellyfinItemId,
            request.MediaIdentity.ItemType,
            source.SourceSha256,
            source.OrientedWidth,
            source.OrientedHeight,
            request.Metadata!.MetadataFingerprint,
            request.ConfigurationFingerprint,
            selection,
            policy,
            request.RendererVersion,
            request.BadgeSchemaVersion);

        return RenderResult.Rendered(
            pngBytes,
            orientedWidth,
            orientedHeight,
            outputHash,
            RenderFingerprint.ComputeOutputFingerprint(fingerprint));
    }

    private static void DrawPill(
        SKCanvas canvas,
        BadgePillPlacement pill,
        SKColor background,
        SKColor text,
        SKPaint pillPaint,
        SKPaint textPaint,
        SKFont font,
        float cornerRadius,
        float horizontalPadding,
        float verticalPadding,
        float ascent)
    {
        pillPaint.Color = background;
        var left = (float)pill.X;
        var top = (float)pill.Y;
        var right = (float)(pill.X + pill.Width);
        var bottom = (float)(pill.Y + pill.Height);
        canvas.DrawRoundRect(new SKRect(left, top, right, bottom), cornerRadius, cornerRadius, pillPaint);

        textPaint.Color = text;
        var baseline = top + verticalPadding - ascent;
        canvas.DrawText(pill.Text, left + horizontalPadding, baseline, font, textPaint);
    }

    private static SKData? LoadFontData(RenderOutputPolicy policy, out RenderFailureReason failure)
    {
        try
        {
            using var stream = policy.FontIdentity.OpenResourceStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            failure = RenderFailureReason.InvalidRequest;
            return SKData.CreateCopy(buffer.ToArray());
        }
        catch (InvalidOperationException)
        {
            failure = RenderFailureReason.FontUnavailable;
            return null;
        }
    }

    private static byte[]? Encode(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null)
        {
            return null;
        }

        using var data = pixmap.Encode(new SKPngEncoderOptions(PngFilterFlags, PngZLibLevel));
        return data?.ToArray();
    }

    private static bool HasMeaningfulAlpha(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque)
        {
            return false;
        }

        var pixels = bitmap.GetPixelSpan();
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 255)
            {
                return true;
            }
        }

        return false;
    }

    private static SKColor ParseColor(string value)
    {
        return RgbColor.TryParse(value, out var color)
            ? new SKColor(color.Red, color.Green, color.Blue)
            : SKColors.Black;
    }

    private static RenderFailureReason MapLimitReason(RenderLimitReason reason)
    {
        return reason switch
        {
            RenderLimitReason.MalformedSource => RenderFailureReason.MalformedSource,
            RenderLimitReason.SourceByteLimitExceeded => RenderFailureReason.SourceByteLimitExceeded,
            RenderLimitReason.SourceDimensionLimitExceeded => RenderFailureReason.SourceDimensionLimitExceeded,
            RenderLimitReason.OutputDimensionLimitExceeded => RenderFailureReason.OutputDimensionLimitExceeded,
            RenderLimitReason.OutputByteLimitExceeded => RenderFailureReason.OutputByteLimitExceeded,
            _ => RenderFailureReason.InvalidRequest,
        };
    }
}
