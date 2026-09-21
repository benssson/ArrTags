using System;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using ArrTags.State;

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
/// before identity immediately before any mutation. When an ArrTags
/// <see cref="ArtworkPublicationState.Published"/> (or captured
/// <see cref="ArtworkPublicationState.NotPublished"/>) session already exists, the
/// render source is the retained original source artifact, never the current
/// active surface, so a repeat publication cannot stack a badge onto a previous
/// ArrTags output. The retained artifact is integrity-validated before use, and a
/// missing, corrupt, or dimension-less baseline fails closed instead of
/// re-capturing the derived image. When no usable session exists the coordinator
/// observes the active surface, which is then the retained baseline. Missing
/// metadata and an ineligible match rely on the renderer's existing pass-through
/// conventions and produce no badge and no mutation. Cancellation is honored and
/// no exception escapes into a Jellyfin operation. No event, queue, or
/// library-scan wiring is added here: Phase 6 drives this entry point.
/// </remarks>
public sealed class ArtworkGenerationCoordinator
{
    private readonly IArtworkSourceReader _reader;
    private readonly IRenderer _renderer;
    private readonly ArtworkPublisher _publisher;
    private readonly PublishedArtworkStateStore _states;
    private readonly SourceArtifactStore _artifacts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkGenerationCoordinator"/> class.
    /// </summary>
    /// <param name="reader">The host-neutral source adapter.</param>
    /// <param name="renderer">The provider-neutral renderer.</param>
    /// <param name="publisher">The durable single-subject publisher.</param>
    /// <param name="states">The authoritative published-artwork state store used for retained-source selection.</param>
    /// <param name="artifacts">The authoritative retained source-artifact store used for retained-source selection.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtworkGenerationCoordinator(
        IArtworkSourceReader reader,
        IRenderer renderer,
        ArtworkPublisher publisher,
        PublishedArtworkStateStore states,
        SourceArtifactStore artifacts)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
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
            // A repeat publication renders from the retained original source
            // artifact, never from the current active surface (which, for an owned
            // session, is a previous ArrTags output). A retained-session failure
            // fails closed rather than falling back to the active surface.
            var retained = ReadRetainedSource(request);
            if (retained.HasSession)
            {
                return retained.Read!;
            }

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

    /// <summary>
    /// Resolves the render source for a subject. A usable owned session returns
    /// the integrity-validated retained original source artifact; a present
    /// session whose baseline is incomplete or corrupt returns a fail-closed
    /// failure; every other state reports that no session applies so the caller
    /// observes the active surface.
    /// </summary>
    private RetainedSourceResolution ReadRetainedSource(ArtworkGenerationRequest request)
    {
        var stateRead = _states.Read(request.JellyfinItemId, request.ImageSurface);
        if (stateRead.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
        {
            return RetainedSourceResolution.Failed(ArtworkSourceReadResult.Failed(
                request.ImageSurface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The published artwork state failed integrity validation."));
        }

        var state = stateRead.Value;
        if (state is null
            || state.State is not (ArtworkPublicationState.Published or ArtworkPublicationState.NotPublished))
        {
            return RetainedSourceResolution.NoSession();
        }

        if (state.SourcePresence == ArtworkImagePresence.Absent)
        {
            return RetainedSourceResolution.Failed(ArtworkSourceReadResult.Absent(request.ImageSurface));
        }

        var capture = state.SourceCaptureIdentity;
        if (state.SourceArtifactId is null
            || capture is not { Width: > 0, Height: > 0 })
        {
            return RetainedSourceResolution.Failed(ArtworkSourceReadResult.Failed(
                request.ImageSurface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The retained source baseline is incomplete."));
        }

        var artifact = _artifacts.Read(state.SourceArtifactId);
        if (artifact.Status != SourceArtifactReadStatus.Found || artifact.Info is null)
        {
            return RetainedSourceResolution.Failed(ArtworkSourceReadResult.Failed(
                request.ImageSurface,
                ArtworkSourceReadFailureReason.Unreadable,
                "The retained source artifact is missing or failed integrity validation."));
        }

        return RetainedSourceResolution.Found(ArtworkSourceReadResult.Present(
            request.ImageSurface,
            artifact.Info.ContentType,
            artifact.Bytes,
            artifact.Info.Sha256,
            capture.Width!.Value,
            capture.Height!.Value,
            capture.DateModifiedUtc,
            capture.JellyfinImageTag));
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

    /// <summary>
    /// The bounded result of resolving the render source: a usable owned session
    /// supplies the read (which may be a fail-closed failure), while no session
    /// means the caller observes the active surface.
    /// </summary>
    private readonly struct RetainedSourceResolution
    {
        private RetainedSourceResolution(bool hasSession, ArtworkSourceReadResult? read)
        {
            HasSession = hasSession;
            Read = read;
        }

        public bool HasSession { get; }

        public ArtworkSourceReadResult? Read { get; }

        public static RetainedSourceResolution NoSession()
        {
            return new RetainedSourceResolution(false, null);
        }

        public static RetainedSourceResolution Found(ArtworkSourceReadResult read)
        {
            return new RetainedSourceResolution(true, read);
        }

        public static RetainedSourceResolution Failed(ArtworkSourceReadResult read)
        {
            return new RetainedSourceResolution(true, read);
        }
    }
}
