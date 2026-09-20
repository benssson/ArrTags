using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;

namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral, single-subject artwork generation coordinator. It
/// composes the host source adapter, the renderer, and the durable publisher for
/// one Jellyfin item and one V1 image surface: it observes the current source,
/// builds the renderer input, renders one derived poster, and only publishes when
/// the render is complete. Every other path - an absent source, an unavailable,
/// unsupported, or oversized source, a render pass-through (including missing or
/// ineligible metadata), a failed render, or a publication that does not complete
/// - leaves the currently usable artwork byte-for-byte unchanged and performs no
/// image mutation. The coordinator never calls Jellyfin directly; the publisher
/// owns all image mutation.
/// </summary>
/// <remarks>
/// The source observed for the render is the exact observation supplied to the
/// publisher's new-session capture, so the retained provenance baseline and the
/// derived artifact describe the same source; the publisher still revalidates the
/// before identity immediately before any mutation. The render source is the
/// observed active surface: selecting the retained original artifact for a repeat
/// publication while an ArrTags session is already owned is an explicit boundary
/// of this task and belongs to the Phase 6 pipeline. Missing metadata and an
/// ineligible match rely on the renderer's existing pass-through conventions and
/// produce no badge and no mutation. Cancellation is honored and no exception
/// escapes into a Jellyfin operation. No event, queue, or library-scan wiring is
/// added here: Phase 6 drives this entry point.
/// </remarks>
public sealed class ArtworkGenerationCoordinator
{
    private readonly IArtworkSourceReader _reader;
    private readonly IRenderer _renderer;
    private readonly ArtworkPublisher _publisher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkGenerationCoordinator"/> class.
    /// </summary>
    /// <param name="reader">The host-neutral source adapter.</param>
    /// <param name="renderer">The provider-neutral renderer.</param>
    /// <param name="publisher">The durable single-subject publisher.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkGenerationCoordinator(
        IArtworkSourceReader reader,
        IRenderer renderer,
        ArtworkPublisher publisher)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    /// <summary>
    /// Generates and, when possible, publishes one derived poster for one
    /// Jellyfin item and V1 surface. The call is bounded and deterministic and
    /// never lets a failure or a cancellation escape as an exception.
    /// </summary>
    /// <param name="request">The canonical generation input.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded generation result.</returns>
    /// <exception cref="ArgumentNullException">The request is <see langword="null"/>.</exception>
    public async Task<ArtworkGenerationResult> GenerateAsync(
        ArtworkGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryValidate(request, out var validationReason))
        {
            return ArtworkGenerationResult.Blocked(validationReason);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ArtworkGenerationResult.Cancelled("The artwork generation was cancelled before it started.");
        }

        try
        {
            return await GenerateCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArtworkGenerationResult.Cancelled("The artwork generation was cancelled.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkGenerationResult.Blocked("The artwork generation could not be completed safely.");
        }
    }

    private async Task<ArtworkGenerationResult> GenerateCoreAsync(
        ArtworkGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var read = await ReadSourceAsync(request, cancellationToken).ConfigureAwait(false);
        if (read.Status == ArtworkSourceReadStatus.Absent)
        {
            // Nothing to render. The current (absent) artwork is preserved and no
            // image mutation is attempted.
            return ArtworkGenerationResult.NoSource("The image surface has no source artwork to render.");
        }

        if (read.Status == ArtworkSourceReadStatus.Failed)
        {
            // The source cannot be used safely; do not call the renderer or the
            // publisher. The classification is bounded and the reader's own text
            // is not propagated.
            return ArtworkGenerationResult.SourceUnavailable(
                read.FailureReason,
                "The active source image could not be used.");
        }

        if (!read.TryCreateSourceImageInput(out var sourceImage) || sourceImage is null)
        {
            return ArtworkGenerationResult.SourceUnavailable(
                read.FailureReason,
                "The active source image could not be described for rendering.");
        }

        var render = await RenderAsync(request, sourceImage, cancellationToken).ConfigureAwait(false);
        if (render.Status == RenderStatus.PassThrough)
        {
            // Missing metadata, an ineligible match, or another ADR-009
            // pass-through: preserve the current artwork and do not publish.
            return ArtworkGenerationResult.PassThrough(
                render.PassThroughReason ?? RenderPassThroughReason.NoDisplayableValue,
                "The render passed through; the current artwork is preserved.");
        }

        if (render.Status == RenderStatus.Failed)
        {
            return ArtworkGenerationResult.RenderFailed(
                render.FailureReason ?? RenderFailureReason.RenderError,
                "The render failed; the current artwork is preserved.");
        }

        if (!render.HasArtifact)
        {
            return ArtworkGenerationResult.RenderFailed(
                RenderFailureReason.RenderError,
                "The rendered result carried no complete artifact.");
        }

        // Only a complete render reaches the publisher. The exact source
        // observation used for the render is supplied so the retained provenance
        // baseline is the same source the derived artifact was produced from.
        var publicationRequest = new ArtworkPublicationRequest(
            request.JellyfinItemId,
            request.ImageSurface,
            render);

        return await PublishAsync(publicationRequest, read, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ArtworkSourceReadResult> ReadSourceAsync(
        ArtworkGenerationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await _reader
                .ReadAsync(request.JellyfinItemId, request.ImageSurface, cancellationToken)
                .ConfigureAwait(false);
            return read ?? ArtworkSourceReadResult.Failed(
                request.ImageSurface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The source adapter returned no bounded result.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkSourceReadResult.Failed(
                request.ImageSurface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The active source image could not be read.");
        }
    }

    private async Task<RenderResult> RenderAsync(
        ArtworkGenerationRequest request,
        SourceImageInput sourceImage,
        CancellationToken cancellationToken)
    {
        try
        {
            var renderRequest = new RenderRequest(
                sourceImage,
                request.MediaIdentity,
                request.Match,
                request.Metadata,
                request.BadgeDefinitions,
                request.ConfigurationFingerprint,
                request.OutputPolicy,
                request.Limits,
                request.RendererVersion,
                request.BadgeSchemaVersion);

            var render = await _renderer.RenderAsync(renderRequest, cancellationToken).ConfigureAwait(false);
            return render ?? RenderResult.Failed(RenderFailureReason.RenderError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return RenderResult.Failed(RenderFailureReason.RenderError);
        }
    }

    private async Task<ArtworkGenerationResult> PublishAsync(
        ArtworkPublicationRequest publicationRequest,
        ArtworkSourceReadResult observedSource,
        CancellationToken cancellationToken)
    {
        ArtworkPublicationResult publication;
        try
        {
            publication = await _publisher
                .PublishAsync(publicationRequest, observedSource, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ArtworkGenerationResult.Blocked("The artwork publication could not be completed safely.");
        }

        if (publication is null)
        {
            return ArtworkGenerationResult.Blocked("The publication returned no bounded result.");
        }

        return publication.Outcome switch
        {
            ArtworkPublicationOutcome.Published => ArtworkGenerationResult.Success(publication),
            ArtworkPublicationOutcome.Cancelled => ArtworkGenerationResult.Cancelled(publication.Reason),
            ArtworkPublicationOutcome.Blocked => ArtworkGenerationResult.BlockedByPublication(publication),
            ArtworkPublicationOutcome.NotEligible => ArtworkGenerationResult.BlockedByPublication(publication),
            ArtworkPublicationOutcome.InvalidRequest => ArtworkGenerationResult.BlockedByPublication(publication),
            _ => ArtworkGenerationResult.PublicationNotCompleted(publication),
        };
    }

    private static bool TryValidate(ArtworkGenerationRequest request, out string reason)
    {
        if (request.JellyfinItemId == Guid.Empty)
        {
            reason = "An artwork generation requires a non-empty Jellyfin item identifier.";
            return false;
        }

        if (request.ImageSurface.Index is not null
            || request.ImageSurface.ImageType != ArtworkImageType.Primary)
        {
            reason = "Only the unindexed Primary image surface is supported.";
            return false;
        }

        if (request.BadgeDefinitions.Count == 0)
        {
            reason = "An artwork generation requires at least one badge definition.";
            return false;
        }

        foreach (var definition in request.BadgeDefinitions)
        {
            if (definition is null)
            {
                reason = "An artwork generation requires non-null badge definitions.";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(request.ConfigurationFingerprint))
        {
            reason = "An artwork generation requires a configuration fingerprint.";
            return false;
        }

        if (request.RendererVersion <= 0 || request.BadgeSchemaVersion <= 0)
        {
            reason = "An artwork generation requires positive renderer and badge schema versions.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
