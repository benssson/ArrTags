using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Metadata;
using ArrTags.Rendering;
using ArrTags.State;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.8 focused checks for the provider-neutral artwork generation
/// coordinator. The host source reader, image writer, and renderer are replaced
/// with injectable doubles so these tests run without a live Jellyfin host,
/// mutate no real library, and prove that every non-published path leaves the
/// current usable artwork byte-for-byte unchanged with no image mutation.
/// </summary>
public sealed class ArtworkGenerationCoordinatorTests : IDisposable
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly Guid Item = RenderTestFixtures.ItemId;

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly FakeArtworkHost _host = new();
    private readonly FakeRenderer _renderer = new();
    private readonly ArtworkPublisher _publisher;
    private readonly ArtworkGenerationCoordinator _coordinator;

    public ArtworkGenerationCoordinatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-generation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        _publisher = new ArtworkPublisher(_host, _host, _artifacts, _states, _operations, new OperationalLimits());
        _coordinator = new ArtworkGenerationCoordinator(_host, _renderer, _publisher);
    }

    // ---- Published path ---------------------------------------------------------

    [Fact]
    public async Task PresentSourceRenderedArtifactIsPublished()
    {
        _host.CurrentBytes = Png(1);
        var derived = Png(2);
        _renderer.Result = Rendered(derived);

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.Published, result.Outcome);
        Assert.True(result.Published);
        Assert.False(result.Preserved);
        Assert.NotNull(result.OperationId);
        Assert.NotNull(result.State);
        Assert.Equal(1, _renderer.Calls);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);
        Assert.Equal(derived, _host.SavedBytes);

        var state = _states.Read(Item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, state.State);
        Assert.Equal(ArtworkHashes.ComputeSha256(derived), state.ActiveImageIdentity!.ContentSha256);
    }

    // ---- Absent source ----------------------------------------------------------

    [Fact]
    public async Task AbsentSourceIsANoOpAndSkipsRendererAndPublisher()
    {
        _host.CurrentBytes = null;

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.NoSource, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Equal(0, _renderer.Calls);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(StateReadStatus.Missing, _states.Read(Item, Surface).Status);
    }

    // ---- Failed source read -----------------------------------------------------

    [Theory]
    [InlineData(ArtworkSourceReadFailureReason.Unreadable)]
    [InlineData(ArtworkSourceReadFailureReason.UnsupportedContentType)]
    [InlineData(ArtworkSourceReadFailureReason.SourceTooLarge)]
    [InlineData(ArtworkSourceReadFailureReason.DimensionTooLarge)]
    public async Task FailedSourceReadPreservesArtworkAndSkipsRenderer(ArtworkSourceReadFailureReason failureReason)
    {
        _host.ReadHandler = _ => ArtworkSourceReadResult.Failed(
            Surface,
            failureReason,
            "host detail that must not leak");

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.SourceUnavailable, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Equal(failureReason, result.SourceFailureReason);
        Assert.Equal(0, _renderer.Calls);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.DoesNotContain("host detail", result.Reason, StringComparison.Ordinal);
    }

    // ---- Render pass-through ----------------------------------------------------

    [Fact]
    public async Task RenderPassThroughPreservesArtworkWithoutPublication()
    {
        _host.CurrentBytes = Png(1);
        _renderer.Result = RenderResult.PassThrough(RenderPassThroughReason.NoDisplayableValue);

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.RenderPassThrough, result.Outcome);
        Assert.Equal(RenderPassThroughReason.NoDisplayableValue, result.PassThroughReason);
        Assert.True(result.Preserved);
        Assert.Equal(1, _renderer.Calls);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(StateReadStatus.Missing, _states.Read(Item, Surface).Status);
    }

    [Fact]
    public async Task NullMetadataPassesThroughByTheRendererConvention()
    {
        // The real renderer's existing convention: absent metadata passes
        // through without decoding or producing an artifact. It touches no native
        // dependency on this path, so the case runs unguarded.
        _host.CurrentBytes = Png(1);
        var coordinator = new ArtworkGenerationCoordinator(_host, new SkiaBadgeRenderer(), _publisher);
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var request = new ArtworkGenerationRequest(
            Item,
            Surface,
            identity,
            RenderTestFixtures.BuildMatch(identity),
            null,
            BadgeDefinition.V1Default,
            "CONFIG-TEST");

        var result = await coordinator.GenerateAsync(request, CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.RenderPassThrough, result.Outcome);
        Assert.Equal(RenderPassThroughReason.NoMetadata, result.PassThroughReason);
        Assert.True(result.Preserved);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
    }

    [Fact]
    public async Task IneligibleMatchPassesThroughByTheRendererConvention()
    {
        var identity = RenderTestFixtures.BuildMovieIdentity();
        var coordinator = new ArtworkGenerationCoordinator(_host, new SkiaBadgeRenderer(), _publisher);
        _host.CurrentBytes = Png(1);

        var result = await coordinator.GenerateAsync(
            Request(identity: identity, match: RenderTestFixtures.BuildMatch(identity, MediaMatchStatus.NotFound)),
            CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.RenderPassThrough, result.Outcome);
        Assert.Equal(RenderPassThroughReason.MatchNotEligible, result.PassThroughReason);
        Assert.True(result.Preserved);
        Assert.Equal(0, _host.SaveCalls);
    }

    // ---- Render failure ---------------------------------------------------------

    [Fact]
    public async Task RenderFailedPreservesArtworkWithoutPublication()
    {
        _host.CurrentBytes = Png(1);
        _renderer.Result = RenderResult.Failed(RenderFailureReason.DecodeFailed);

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.RenderFailed, result.Outcome);
        Assert.Equal(RenderFailureReason.DecodeFailed, result.FailureReason);
        Assert.True(result.Preserved);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(StateReadStatus.Missing, _states.Read(Item, Surface).Status);
    }

    [Fact]
    public async Task RendererExceptionIsContainedAndPreservesArtwork()
    {
        _host.CurrentBytes = Png(1);
        _renderer.Throw = new InvalidOperationException("renderer detail that must not leak");

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.RenderFailed, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Equal(0, _host.SaveCalls);
        Assert.DoesNotContain("renderer detail", result.Reason, StringComparison.Ordinal);
    }

    // ---- Publication not completed ----------------------------------------------

    [Fact]
    public async Task PublicationFailurePreservesArtwork()
    {
        _host.CurrentBytes = Png(1);
        _host.SaveAppliesBytes = false; // readback observes the unchanged source
        _renderer.Result = Rendered(Png(2));

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.PublicationNotCompleted, result.Outcome);
        Assert.True(result.Preserved);
        Assert.NotNull(result.PublicationOutcome);
        Assert.Equal(1, _host.SaveCalls);
        Assert.NotEqual(ArtworkPublicationState.Published, _states.Read(Item, Surface).Value?.State ?? ArtworkPublicationState.NotPublished);
    }

    [Fact]
    public async Task BlockedPublicationMapsToBlockedWithoutMutation()
    {
        _host.CurrentBytes = Png(1);
        _renderer.Result = Rendered(Png(2));
        _operations.Write(ArtworkOperationFixtures.Publication(item: Item, surface: Surface, phase: ArtworkOperationPhase.Prepared));

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.Blocked, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, result.PublicationOutcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
    }

    // ---- Cancellation -----------------------------------------------------------

    [Fact]
    public async Task CancelledBeforeStartPreservesArtwork()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var result = await _coordinator.GenerateAsync(Request(), source.Token);

        Assert.Equal(ArtworkGenerationOutcome.Cancelled, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Equal(0, _renderer.Calls);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task CancellationDuringSourceReadIsHonored()
    {
        _host.ReadThrows = new OperationCanceledException();

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, _renderer.Calls);
        Assert.Equal(0, _host.SaveCalls);
    }

    // ---- Bounded input ----------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidRequestIsBlockedWithoutMutation(bool emptyItem)
    {
        var request = emptyItem
            ? Request(item: Guid.Empty)
            : Request(surface: new ArtworkImageSurface(ArtworkImageType.Primary, 1));

        var result = await _coordinator.GenerateAsync(request, CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.Blocked, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Equal(0, _renderer.Calls);
        Assert.Equal(0, _host.SaveCalls);
    }

    // ---- Source/render/Provenance consistency -----------------------------------

    [Fact]
    public async Task SourceChangedAfterTheRenderObservationAbortsWithoutMutation()
    {
        _renderer.Result = Rendered(Png(2));
        _host.ReadHandler = index => index == 0 ? Present(Png(1)) : Present(Png(9));

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        // The publisher captured the coordinator's observation (Png(1)) and then
        // revalidated immediately before mutation, observing an external change.
        Assert.Equal(ArtworkGenerationOutcome.PublicationNotCompleted, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
    }

    [Fact]
    public async Task PublicationCancellationIsContainedAndPreservesArtwork()
    {
        _host.CurrentBytes = Png(1);
        _host.ThrowOnSave = true;
        _renderer.Result = Rendered(Png(2));

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.Cancelled, result.Outcome);
        Assert.True(result.Preserved);
        Assert.Null(_host.SavedBytes);
        Assert.NotEqual(ArtworkPublicationState.Published, _states.Read(Item, Surface).Value?.State ?? ArtworkPublicationState.NotPublished);
    }

    [Fact]
    public async Task SourceObservationIsSharedWithTheProvenanceCapture()
    {
        var source = Png(1);
        _host.CurrentBytes = source;
        _renderer.Result = Rendered(Png(2));

        var result = await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ArtworkGenerationOutcome.Published, result.Outcome);

        // One coordinator read plus the publisher's before-mutation revalidation
        // and readback. The publisher performs no independent capture read, so the
        // render and the retained baseline share one observation.
        Assert.Equal(3, _host.ReadCalls);

        var state = _states.Read(Item, Surface).Value!;
        Assert.Equal(ArtworkHashes.ComputeSha256(source), state.SourceFingerprint);
        var retained = _artifacts.Read(state.SourceArtifactId!);
        Assert.Equal(SourceArtifactReadStatus.Found, retained.Status);
        Assert.Equal(source, retained.Bytes.ToArray());

        // The renderer was given the exact observed source bytes.
        Assert.Equal(ArtworkHashes.ComputeSha256(source), _renderer.LastRequest!.SourceImage!.SourceSha256);
    }

    // ---- State and boundary hygiene ---------------------------------------------

    [Fact]
    public async Task PersistedStateAndOperationContainNoPathOrHostDetail()
    {
        _host.CurrentBytes = Png(1);
        _renderer.Result = Rendered(Png(2));
        await _coordinator.GenerateAsync(Request(), CancellationToken.None);

        var statePath = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            PublishedArtworkStateStore.GetRecordId(Item, Surface));
        var stateJson = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath));

        Assert.DoesNotContain(_root, stateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Path", stateJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorBoundaryExposesNoJellyfinTypesOrPaths()
    {
        AssertNoHostTypeLeak(typeof(ArtworkGenerationRequest));
        AssertNoHostTypeLeak(typeof(ArtworkGenerationResult));
        AssertNoHostTypeLeak(typeof(ArtworkGenerationCoordinator));
    }

    [Fact]
    public void CoordinatorAndRendererAreRegisteredWithoutStartupWork()
    {
        var services = new ServiceCollection();
        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        var renderer = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRenderer));
        Assert.NotNull(renderer.ImplementationFactory);

        var coordinator = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ArtworkGenerationCoordinator));
        Assert.NotNull(coordinator.ImplementationFactory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ArtworkGenerationRequest Request(
        MediaIdentity? identity = null,
        MediaMatch? match = null,
        BadgeMetadata? metadata = null,
        Guid? item = null,
        ArtworkImageSurface? surface = null)
    {
        var resolvedIdentity = identity ?? RenderTestFixtures.BuildMovieIdentity();
        return new ArtworkGenerationRequest(
            item ?? Item,
            surface ?? Surface,
            resolvedIdentity,
            match ?? RenderTestFixtures.BuildMatch(resolvedIdentity),
            metadata ?? RenderTestFixtures.BuildMetadata(),
            BadgeDefinition.V1Default,
            "CONFIG-TEST");
    }

    private static RenderResult Rendered(byte[] derived)
    {
        return RenderResult.Rendered(
            derived,
            100,
            150,
            ArtworkHashes.ComputeSha256(derived),
            Fingerprint(derived));
    }

    private static string Fingerprint(byte[] bytes)
    {
        return ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("fingerprint-" + ArtworkHashes.ComputeSha256(bytes)));
    }

    private static void AssertNoHostTypeLeak(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.NotEqual("Path", property.Name);
            Assert.False(
                (property.PropertyType.Namespace ?? string.Empty).StartsWith("MediaBrowser", StringComparison.Ordinal),
                $"{type.Name}.{property.Name} leaks a Jellyfin type.");
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.False(
                (method.ReturnType.Namespace ?? string.Empty).StartsWith("MediaBrowser", StringComparison.Ordinal),
                $"{type.Name}.{method.Name} returns a Jellyfin type.");
            foreach (var parameter in method.GetParameters())
            {
                Assert.False(
                    (parameter.ParameterType.Namespace ?? string.Empty).StartsWith("MediaBrowser", StringComparison.Ordinal),
                    $"{type.Name}.{method.Name} accepts a Jellyfin type.");
            }
        }
    }

    private static byte[] Png(byte marker)
    {
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, marker, marker];
    }

    private static ArtworkSourceReadResult Present(byte[] bytes)
    {
        return ArtworkSourceReadResult.Present(
            Surface,
            "image/png",
            bytes,
            ArtworkHashes.ComputeSha256(bytes),
            100,
            150,
            DateTimeOffset.UnixEpoch,
            "tag-1");
    }

    private sealed class FakeRenderer : IRenderer
    {
        public RenderResult Result { get; set; } = RenderResult.PassThrough(RenderPassThroughReason.NoMetadata);

        public Exception? Throw { get; set; }

        public int Calls { get; private set; }

        public RenderRequest? LastRequest { get; private set; }

        public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeArtworkHost : IArtworkSourceReader, IArtworkImageWriter
    {
        public byte[]? CurrentBytes { get; set; }

        public bool SaveAppliesBytes { get; set; } = true;

        public bool FailUpdate { get; set; }

        public bool ThrowOnSave { get; set; }

        public Func<int, ArtworkSourceReadResult>? ReadHandler { get; set; }

        public Exception? ReadThrows { get; set; }

        public int ReadCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public byte[]? SavedBytes { get; private set; }

        public ArtworkSourceReadResult Current(ArtworkImageSurface surface)
        {
            return CurrentBytes is null
                ? ArtworkSourceReadResult.Absent(surface)
                : ArtworkSourceReadResult.Present(
                    surface,
                    "image/png",
                    CurrentBytes,
                    ArtworkHashes.ComputeSha256(CurrentBytes),
                    100,
                    150,
                    DateTimeOffset.UnixEpoch,
                    "tag-1");
        }

        public Task<ArtworkSourceReadResult> ReadAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = ReadCalls++;
            if (ReadThrows is not null)
            {
                throw ReadThrows;
            }

            var result = ReadHandler is not null ? ReadHandler(index) : Current(surface);
            return Task.FromResult(result);
        }

        public Task<ArtworkImageMutationResult> SaveImageAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            ReadOnlyMemory<byte> content,
            string contentType,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            if (ThrowOnSave)
            {
                throw new OperationCanceledException();
            }

            SavedBytes = content.ToArray();
            if (SaveAppliesBytes)
            {
                CurrentBytes = content.ToArray();
            }

            return Task.FromResult(ArtworkImageMutationResult.Success());
        }

        public Task<ArtworkImageMutationResult> PersistItemUpdateAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCalls++;
            return Task.FromResult(FailUpdate
                ? ArtworkImageMutationResult.Failure(ArtworkImageMutationStatus.Failed, "The fake update failed.")
                : ArtworkImageMutationResult.Success());
        }
    }
}
