using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Rendering;
using ArrTags.State;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.5 focused checks for the durable publication orchestration and
/// the single Jellyfin image-mutation boundary. The host source reader and image
/// writer are replaced with injectable doubles, so these tests run without a live
/// Jellyfin host, mutate no real library, and exercise the write-ahead ordering,
/// before-identity revalidation, absent baseline handling, repeat publication,
/// readback verification, bounded cancellation, and state hygiene.
/// </summary>
public sealed class ArtworkPublisherTests : IDisposable
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly FakeArtworkHost _host;
    private readonly ArtworkPublisher _publisher;
    private readonly Guid _item = Guid.NewGuid();

    public ArtworkPublisherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        _host = new FakeArtworkHost();
        _publisher = CreatePublisher(new OperationalLimits());
        _host.PhaseProbe = () => _operations.Read(_item, Surface).Value!.Phase;
    }

    // ---- Happy path ------------------------------------------------------------

    [Fact]
    public async Task HappyPathPublishesVerifiesAndCommits()
    {
        var source = Png(1);
        var derived = Png(2);
        _host.CurrentBytes = source;

        var result = await _publisher.PublishAsync(Request(derived), CancellationToken.None);

        Assert.True(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.Published, result.Outcome);
        Assert.NotNull(result.OperationId);

        // The supported mutation boundary received the exact durable derived bytes.
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);
        Assert.Equal("image/png", _host.SavedContentType);
        Assert.Equal(derived, _host.SavedBytes);

        // The write-ahead phases were durable before the external calls.
        Assert.Equal(ArtworkOperationPhase.MutationStarted, _host.PhaseAtSave);
        Assert.Equal(ArtworkOperationPhase.RepositoryUpdateStarted, _host.PhaseAtUpdate);

        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Committed, operation.Phase);
        Assert.Equal(ArtworkHashes.ComputeSha256(derived), operation.ObservedAfterIdentity!.ContentSha256);
        Assert.Equal(result.OperationId, operation.OperationId);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, state.State);
        Assert.Equal(ArtworkImagePresence.Present, state.SourcePresence);
        Assert.Equal(ArtworkHashes.ComputeSha256(derived), state.ActiveImageIdentity!.ContentSha256);
        Assert.True(ArtworkTokens.IsValid(state.OwnershipToken));
        Assert.True(ArtworkTokens.IsValid(state.PublicationToken));
        Assert.Equal(result.OperationId, state.LastOperationId);
        Assert.Equal(2, state.StateRevision);

        // The original source remains retained under its content-addressed id.
        Assert.True(ArtworkHashes.IsSha256Hex(state.SourceArtifactId));
        var retained = _artifacts.Read(state.SourceArtifactId!);
        Assert.Equal(SourceArtifactReadStatus.Found, retained.Status);
        Assert.Equal(source, retained.Bytes.ToArray());
        Assert.Equal(ArtworkHashes.ComputeSha256(source), state.SourceFingerprint);
    }

    [Fact]
    public async Task AbsentBaselineIsCapturedExplicitlyAndPublished()
    {
        _host.CurrentBytes = null;
        var derived = Png(3);

        var result = await _publisher.PublishAsync(Request(derived), CancellationToken.None);

        Assert.True(result.Published);
        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, state.State);
        Assert.Equal(ArtworkImagePresence.Absent, state.SourcePresence);
        Assert.Null(state.SourceArtifactId);
        Assert.Null(state.SourceFingerprint);
        Assert.Equal(ArtworkImagePresence.Absent, state.SourceCaptureIdentity!.Presence);
        Assert.Equal(ArtworkHashes.ComputeSha256(derived), state.ActiveImageIdentity!.ContentSha256);
    }

    // ---- Before-identity revalidation -------------------------------------------

    [Fact]
    public async Task BeforeIdentityMismatchAbortsWithoutAnyImageMutation()
    {
        _host.CurrentBytes = Png(1);
        _host.ReadHandler = index => index == 0 ? _host.Current(Surface) : Current(Png(9));

        var result = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.BeforeIdentityChanged, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);

        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Aborted, operation.Phase);

        // A not-yet-published session has no ownership to lose, so no final state is written.
        Assert.Equal(StateReadStatus.Missing, _states.Read(_item, Surface).Status);
    }

    [Fact]
    public async Task UnobservableBeforeIdentityAbortsWithoutAnyImageMutation()
    {
        _host.CurrentBytes = Png(1);
        _host.ReadHandler = index => index == 0
            ? _host.Current(Surface)
            : ArtworkSourceReadResult.Failed(Surface, ArtworkSourceReadFailureReason.Unreadable, "unobservable");

        var result = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.BeforeIdentityUnknown, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(ArtworkOperationPhase.Aborted, _operations.Read(_item, Surface).Value!.Phase);
    }

    // ---- Readback verification --------------------------------------------------

    [Fact]
    public async Task ReadbackMismatchLeavesTheArtworkUncommitted()
    {
        _host.CurrentBytes = Png(1);
        _host.SaveAppliesBytes = false;

        var result = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.ReadbackMismatch, result.Outcome);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);

        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.RecoveryBlocked, operation.Phase);
        Assert.NotNull(operation.ObservedAfterIdentity);
        Assert.Equal(StateReadStatus.Missing, _states.Read(_item, Surface).Status);

        // A recovery-blocked operation blocks new automatic publication until 5.7 reconciles it.
        var retry = await _publisher.PublishAsync(Request(Png(4)), CancellationToken.None);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, retry.Outcome);
        Assert.Equal(1, _host.SaveCalls);
    }

    [Fact]
    public async Task ItemUpdateFailureLeavesTheOperationRecoveryBlocked()
    {
        _host.CurrentBytes = Png(1);
        _host.FailUpdate = true;

        var result = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.RepositoryUpdateFailed, result.Outcome);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(ArtworkOperationPhase.RecoveryBlocked, _operations.Read(_item, Surface).Value!.Phase);
        Assert.Equal(StateReadStatus.Missing, _states.Read(_item, Surface).Status);
    }

    // ---- Repeat publication and ownership ---------------------------------------

    [Fact]
    public async Task RepeatPublicationReusesSourceAndOwnershipTokenWithANewPublicationToken()
    {
        _host.CurrentBytes = Png(1);
        var first = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);
        var firstState = _states.Read(_item, Surface).Value!;

        var second = await _publisher.PublishAsync(Request(Png(3)), CancellationToken.None);
        var secondState = _states.Read(_item, Surface).Value!;

        Assert.True(first.Published);
        Assert.True(second.Published);
        Assert.Equal(firstState.OwnershipToken, secondState.OwnershipToken);
        Assert.Equal(firstState.SourceArtifactId, secondState.SourceArtifactId);
        Assert.Equal(ArtworkHashes.ComputeSha256(Png(1)), secondState.SourceFingerprint);
        Assert.NotEqual(firstState.PublicationToken, secondState.PublicationToken);
        Assert.Equal(ArtworkHashes.ComputeSha256(Png(3)), secondState.ActiveImageIdentity!.ContentSha256);
        Assert.Equal(3, secondState.StateRevision);

        // The first derivative was never captured as a new source.
        Assert.Equal(Png(1), _artifacts.Read(secondState.SourceArtifactId!).Bytes.ToArray());

        var secondOperation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(2, secondOperation.Generation);
        Assert.Equal(firstState.PublicationToken, secondOperation.PriorPublicationToken);
    }

    [Fact]
    public async Task LostOwnershipIsRecordedAndBlocksPublication()
    {
        _host.CurrentBytes = Png(1);
        await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        // An external actor replaced the active image after the first publication.
        _host.CurrentBytes = Png(9);
        var saveCalls = _host.SaveCalls;

        var result = await _publisher.PublishAsync(Request(Png(3)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.BeforeIdentityChanged, result.Outcome);
        Assert.Equal(saveCalls, _host.SaveCalls);
        Assert.Equal(ArtworkPublicationState.OwnershipLost, _states.Read(_item, Surface).Value!.State);
    }

    [Fact]
    public async Task StaleNotPublishedBaselineRequiresARecapture()
    {
        var capturedBytes = Png(1);
        _host.CurrentBytes = capturedBytes;
        var captureIdentity = _host.Current(Surface) is { } current && current.TryCreateActiveImageIdentity(out var identity)
            ? identity!
            : throw new InvalidOperationException("Expected a present identity.");
        var artifact = ArtworkStateFixtures.Artifact(capturedBytes);
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, _item, Surface, captureIdentity, artifact, DateTimeOffset.UtcNow)
            .State;
        _states.Write(session);

        _host.CurrentBytes = Png(9);
        var result = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.RecaptureRequired, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task NonTerminalOperationBlocksNewPublication()
    {
        _operations.Write(ArtworkOperationFixtures.Publication(item: _item, surface: Surface, phase: ArtworkOperationPhase.Prepared));

        var result = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task UnresolvedSourceReadFailsClosed()
    {
        _host.ReadHandler = _ => ArtworkSourceReadResult.Failed(
            Surface,
            ArtworkSourceReadFailureReason.Unreadable,
            "host detail that must not leak");

        var result = await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.SourceUnavailable, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.DoesNotContain("host detail", result.Reason, StringComparison.Ordinal);
    }

    // ---- Bounds and cancellation -------------------------------------------------

    [Fact]
    public async Task CancellationBeforePublicationDoesNotMutate()
    {
        _host.CurrentBytes = Png(1);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var result = await _publisher.PublishAsync(Request(Png(2)), source.Token);

        Assert.Equal(ArtworkPublicationOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
    }

    [Fact]
    public async Task OversizedDerivedArtifactIsRejectedBeforeMutation()
    {
        _host.CurrentBytes = Png(1);
        var limited = CreatePublisher(new OperationalLimits { DerivedArtifactLimitBytes = 64L * 1024 });
        var derived = new byte[64 * 1024 + 1];
        Png(1).CopyTo(derived, 0);

        var result = await limited.PublishAsync(Request(derived), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.DerivedArtifactRejected, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task MismatchedDerivedHashIsRejectedBeforeMutation()
    {
        _host.CurrentBytes = Png(1);
        var derived = Png(2);
        var rendered = RenderResult.Rendered(
            derived,
            100,
            150,
            ArtworkHashes.ComputeSha256(Png(7)),
            Fingerprint(derived));

        var result = await _publisher.PublishAsync(
            new ArtworkPublicationRequest(_item, Surface, rendered),
            CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.DerivedArtifactRejected, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    // ---- State hygiene -----------------------------------------------------------

    [Fact]
    public async Task PersistedStateAndOperationContainNoPathOrHostDetail()
    {
        _host.CurrentBytes = Png(1);
        await _publisher.PublishAsync(Request(Png(2)), CancellationToken.None);

        var statePath = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            PublishedArtworkStateStore.GetRecordId(_item, Surface));
        var operationPath = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkOperationStore.RecordKind,
            ArtworkOperationStore.GetRecordId(_item, Surface));

        var stateJson = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(statePath));
        var operationJson = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(operationPath));

        Assert.DoesNotContain(_root, stateJson, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, operationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Path", stateJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationBoundaryExposesNoJellyfinTypesOrPaths()
    {
        AssertNoHostTypeLeak(typeof(ArtworkPublicationRequest));
        AssertNoHostTypeLeak(typeof(ArtworkPublicationResult));
        AssertNoHostTypeLeak(typeof(ArtworkImageMutationResult));
        AssertNoHostTypeLeak(typeof(IArtworkImageWriter));
        AssertNoHostTypeLeak(typeof(ArtworkPublisher));
    }

    // ---- Host boundary -----------------------------------------------------------

    [Fact]
    public void PublisherAndImageWriterAreRegisteredWithoutStartupWork()
    {
        var services = new ServiceCollection();
        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        var writer = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IArtworkImageWriter));
        Assert.NotNull(writer.ImplementationFactory);

        var publisher = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ArtworkPublisher));
        Assert.NotNull(publisher.ImplementationFactory);
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

    private ArtworkPublisher CreatePublisher(OperationalLimits limits)
    {
        return new ArtworkPublisher(_host, _host, _artifacts, _states, _operations, limits);
    }

    private ArtworkPublicationRequest Request(byte[] derived)
    {
        return new ArtworkPublicationRequest(_item, Surface, RenderResult.Rendered(
            derived,
            100,
            150,
            ArtworkHashes.ComputeSha256(derived),
            Fingerprint(derived)));
    }

    private static ArtworkSourceReadResult Current(byte[] bytes)
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

    private sealed class FakeArtworkHost : IArtworkSourceReader, IArtworkImageWriter
    {
        public byte[]? CurrentBytes { get; set; }

        public bool SaveAppliesBytes { get; set; } = true;

        public bool FailSave { get; set; }

        public bool FailUpdate { get; set; }

        public Func<int, ArtworkSourceReadResult>? ReadHandler { get; set; }

        public Func<ArtworkOperationPhase>? PhaseProbe { get; set; }

        public int ReadCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public string? SavedContentType { get; private set; }

        public byte[]? SavedBytes { get; private set; }

        public ArtworkOperationPhase? PhaseAtSave { get; private set; }

        public ArtworkOperationPhase? PhaseAtUpdate { get; private set; }

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
            PhaseAtSave = PhaseProbe?.Invoke();
            if (FailSave)
            {
                return Task.FromResult(ArtworkImageMutationResult.Failure(
                    ArtworkImageMutationStatus.Failed,
                    "The fake save failed."));
            }

            SavedContentType = contentType;
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
            PhaseAtUpdate = PhaseProbe?.Invoke();
            return Task.FromResult(FailUpdate
                ? ArtworkImageMutationResult.Failure(ArtworkImageMutationStatus.Failed, "The fake update failed.")
                : ArtworkImageMutationResult.Success());
        }
    }
}

/// <summary>
/// Phase 5 task 5.5 checks for the single Jellyfin image-mutation implementation.
/// The Jellyfin managers are replaced with <see cref="DispatchProxy"/> doubles so
/// the stream <c>SaveImage</c> overload and the normal item update are exercised
/// without a live host and without mutating a real library.
/// </summary>
public sealed class JellyfinArtworkImageWriterTests
{
    private static readonly Guid Item = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

    [Fact]
    public async Task SaveImageUsesTheStreamOverloadAndNeverThePathOrUrlOverload()
    {
        var (writer, library, provider) = Create();
        var item = new TestMovie();
        library.Items = _ => item;

        var result = await writer.SaveImageAsync(Item, Surface, PngBytes, "image/png", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, provider.StreamCalls);
        Assert.Equal(0, provider.PathCalls);
        Assert.Equal(0, provider.UrlCalls);
        Assert.Equal("image/png", provider.MimeType);
        Assert.Equal(ImageType.Primary, provider.SavedImageType);
        Assert.Null(provider.ImageIndex);
        Assert.Equal(PngBytes, provider.Bytes);
    }

    [Fact]
    public async Task PersistItemUpdateRunsTheNormalImageUpdateFlow()
    {
        var (writer, library, _) = Create();
        var item = new TestMovie();
        library.Items = _ => item;

        var result = await writer.PersistItemUpdateAsync(Item, Surface, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, item.UpdateCalls);
        Assert.Equal(ItemUpdateType.ImageUpdate, item.LastUpdateType);
    }

    [Fact]
    public async Task MissingItemFailsClosedWithoutProviderCall()
    {
        var (writer, library, provider) = Create();
        library.Items = _ => null;

        var save = await writer.SaveImageAsync(Item, Surface, PngBytes, "image/png", CancellationToken.None);
        var update = await writer.PersistItemUpdateAsync(Item, Surface, CancellationToken.None);

        Assert.False(save.Succeeded);
        Assert.Equal(ArtworkImageMutationStatus.ItemNotFound, save.Status);
        Assert.False(update.Succeeded);
        Assert.Equal(ArtworkImageMutationStatus.ItemNotFound, update.Status);
        Assert.Equal(0, provider.StreamCalls);
    }

    [Fact]
    public async Task UnsupportedSurfaceAndContentFailClosed()
    {
        var (writer, library, provider) = Create();
        library.Items = _ => new TestMovie();
        var indexed = new ArtworkImageSurface(ArtworkImageType.Primary, 1);

        var save = await writer.SaveImageAsync(Item, indexed, PngBytes, "image/png", CancellationToken.None);
        var invalid = await writer.SaveImageAsync(Item, Surface, PngBytes, "image/webp", CancellationToken.None);
        var empty = await writer.SaveImageAsync(Item, Surface, ReadOnlyMemory<byte>.Empty, "image/png", CancellationToken.None);

        Assert.Equal(ArtworkImageMutationStatus.UnsupportedSurface, save.Status);
        Assert.Equal(ArtworkImageMutationStatus.InvalidContent, invalid.Status);
        Assert.Equal(ArtworkImageMutationStatus.InvalidContent, empty.Status);
        Assert.Equal(0, provider.StreamCalls);
    }

    [Fact]
    public async Task ProviderExceptionIsMappedToABoundedFailure()
    {
        var (writer, library, provider) = Create();
        library.Items = _ => new TestMovie();
        provider.Throw = true;

        var result = await writer.SaveImageAsync(Item, Surface, PngBytes, "image/png", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ArtworkImageMutationStatus.Failed, result.Status);
        Assert.DoesNotContain("fake", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsHonored()
    {
        var (writer, library, provider) = Create();
        library.Items = _ => new TestMovie();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => writer.SaveImageAsync(Item, Surface, PngBytes, "image/png", source.Token));
        Assert.Equal(0, provider.StreamCalls);
    }

    private static (JellyfinArtworkImageWriter Writer, FakeLibraryManager Library, FakeProviderManager Provider) Create()
    {
        var library = DispatchProxy.Create<ILibraryManager, FakeLibraryManager>();
        var libraryFake = (FakeLibraryManager)(object)library;
        var provider = DispatchProxy.Create<IProviderManager, FakeProviderManager>();
        var providerFake = (FakeProviderManager)(object)provider;
        return (new JellyfinArtworkImageWriter(library, provider), libraryFake, providerFake);
    }

    private sealed class TestMovie : Movie
    {
        public int UpdateCalls { get; private set; }

        public ItemUpdateType LastUpdateType { get; private set; }

        public override Task UpdateToRepositoryAsync(ItemUpdateType updateReason, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            LastUpdateType = updateReason;
            return Task.CompletedTask;
        }
    }

    public class FakeLibraryManager : DispatchProxy
    {
        public Func<Guid, BaseItem?>? Items { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemById))
            {
                return Items?.Invoke((Guid)args![0]!);
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class FakeProviderManager : DispatchProxy
    {
        public bool Throw { get; set; }

        public int StreamCalls { get; private set; }

        public int PathCalls { get; private set; }

        public int UrlCalls { get; private set; }

        public string? MimeType { get; private set; }

        public ImageType? SavedImageType { get; private set; }

        public int? ImageIndex { get; private set; }

        public byte[]? Bytes { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IProviderManager.SaveImage))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            if (Throw)
            {
                throw new InvalidOperationException("The fake provider failed.");
            }

            if (args![1] is Stream stream)
            {
                StreamCalls++;
                MimeType = (string)args[2]!;
                SavedImageType = (MediaBrowser.Model.Entities.ImageType)args[3]!;
                ImageIndex = (int?)args[4];
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                Bytes = buffer.ToArray();
                return Task.CompletedTask;
            }

            if (args[1] is string)
            {
                PathCalls++;
                return Task.CompletedTask;
            }

            UrlCalls++;
            return Task.CompletedTask;
        }
    }
}
