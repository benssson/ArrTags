using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Rendering;
using ArrTags.State;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.9 focused checks for the durable lifecycle fence, the
/// disable/uninstall drain and guarded restoration, the supported image-removal
/// primitive, and confirmed item-removal tombstoning. The host source reader and
/// image writer are injectable doubles, so these tests run without a live
/// Jellyfin host, mutate no real library, and prove that a blocked, changed, or
/// uncertain restoration leaves the active image and its recovery records in
/// place while reporting an incomplete lifecycle result.
/// </summary>
public sealed class ArtworkLifecycleTests : IDisposable
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(5);

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly ArtworkLifecycleFenceStore _fences;
    private readonly FakeArtworkHost _host;
    private readonly FakePluginLifecycleFenceProvider _pluginLifecycle;
    private readonly ArtworkPublisher _publisher;
    private readonly ArtworkReconciler _reconciler;
    private readonly ArtworkLifecycleCoordinator _coordinator;
    private readonly Guid _item = Guid.NewGuid();
    private readonly byte[] _source;
    private readonly byte[] _derived;
    private readonly byte[] _external;
    private readonly string _sourceArtifactId;
    private readonly string _derivedArtifactId;

    public ArtworkLifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        _fences = new ArtworkLifecycleFenceStore(_repository);
        _host = new FakeArtworkHost();
        _pluginLifecycle = new FakePluginLifecycleFenceProvider();
        _publisher = new ArtworkPublisher(_host, _host, _artifacts, _states, _operations, new OperationalLimits(), _fences);
        _reconciler = new ArtworkReconciler(_host, _states, _operations, _publisher);
        _coordinator = new ArtworkLifecycleCoordinator(
            _host,
            _states,
            _operations,
            _reconciler,
            _publisher,
            _fences,
            _pluginLifecycle);

        _source = Png(0x11);
        _derived = Png(0x22);
        _external = Png(0x33);
        _sourceArtifactId = Promote(_source);
        _derivedArtifactId = Promote(_derived);
        _host.CurrentBytes = _source;
    }

    // ---- Durable fence store --------------------------------------------------

    [Fact]
    public void FenceSurvivesAReconstructedRepository()
    {
        _fences.Set(ArtworkLifecycleFence.Uninstall, "uninstalling");

        var reopened = new ArtworkLifecycleFenceStore(new StateRepository(_root));
        var state = reopened.Read();

        Assert.True(state.IsValid);
        Assert.Equal(ArtworkLifecycleFence.Uninstall, state.Fence);
        Assert.False(state.AllowsNewPublication);
        Assert.True(state.AllowsNewRestoration);
    }

    [Fact]
    public void MissingFenceIsNormal()
    {
        var state = _fences.Read();

        Assert.True(state.IsValid);
        Assert.Equal(ArtworkLifecycleFence.Normal, state.Fence);
        Assert.True(state.AllowsNewPublication);
    }

    [Fact]
    public void InvalidFenceFailsClosed()
    {
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkLifecycleFenceStore.RecordKind,
            ArtworkLifecycleFenceStore.ActiveRecordId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json");

        var state = _fences.Read();

        Assert.False(state.IsValid);
        Assert.False(state.AllowsNewPublication);
    }

    [Fact]
    public void ResetClearsAStaleFenceOnlyWhenNotNormal()
    {
        _fences.Set(ArtworkLifecycleFence.Disable, "disabled");
        _coordinator.ResetStaleFence();

        Assert.Equal(ArtworkLifecycleFence.Normal, _fences.Read().Fence);
    }

    [Fact]
    public void ResetPreservesAnInvalidFenceInsteadOfOverwritingItWithNormal()
    {
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkLifecycleFenceStore.RecordKind,
            ArtworkLifecycleFenceStore.ActiveRecordId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json");

        _coordinator.ResetStaleFence();

        var state = _fences.Read();
        Assert.False(state.IsValid);
        Assert.False(state.AllowsNewPublication);
        Assert.True(File.Exists(path), "A corrupt fence must be preserved so publication stays fail-closed.");
    }

    // ---- New publication refused under a non-normal fence ----------------------

    [Theory]
    [InlineData(ArtworkLifecycleFence.Disable)]
    [InlineData(ArtworkLifecycleFence.Uninstall)]
    [InlineData(ArtworkLifecycleFence.ItemRemoved)]
    public async Task PublisherRefusesNewPublicationUnderANonNormalFence(ArtworkLifecycleFence fence)
    {
        _fences.Set(fence, "fenced");
        _host.CurrentBytes = _source;

        var result = await _publisher.PublishAsync(Request(_derived), CancellationToken.None);

        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
        Assert.Equal(StateReadStatus.Missing, _operations.Read(_item, Surface).Status);
    }

    [Fact]
    public async Task PublisherRefusesNewPublicationWhenTheFenceRecordIsInvalid()
    {
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkLifecycleFenceStore.RecordKind,
            ArtworkLifecycleFenceStore.ActiveRecordId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ this is not valid json");
        _host.CurrentBytes = _source;

        var result = await _publisher.PublishAsync(Request(_derived), CancellationToken.None);

        Assert.Equal(ArtworkPublicationOutcome.Blocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    // ---- Guarded restoration ---------------------------------------------------

    [Fact]
    public async Task DisableDrainRestoresTheRetainedSourceForAPresentBaseline()
    {
        _states.Write(PublishedPresentState());
        _host.CurrentBytes = _derived;

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Disable, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.RestorationsCompleted);
        Assert.Equal(0, result.RestorationsBlocked);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(_source, _host.SavedBytes);
        Assert.Equal("image/png", _host.SavedContentType);
        Assert.Equal(0, _host.RemoveCalls);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Restored, state.State);

        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationKind.Restoration, operation.Kind);
        Assert.Equal(ArtworkOperationPhase.Committed, operation.Phase);
        Assert.Equal(ArtworkHashes.ComputeSha256(_source), operation.CandidateAfterContentSha256);
    }

    [Fact]
    public async Task UninstallDrainRemovesTheArrtagsImageForAnAbsentBaseline()
    {
        _states.Write(PublishedAbsentState());
        _host.CurrentBytes = _derived;

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.RestorationsCompleted);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(1, _host.RemoveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Null(_host.CurrentBytes);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Restored, state.State);
        Assert.Equal(ArtworkImagePresence.Absent, state.SourcePresence);
        Assert.Equal(ArtworkOperationPhase.Committed, _operations.Read(_item, Surface).Value!.Phase);
    }

    [Fact]
    public async Task ExternallyChangedImageBlocksRestorationAndRetainsEverything()
    {
        var published = PublishedPresentState();
        _states.Write(published);
        _host.CurrentBytes = _external;

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Incomplete, result.Outcome);
        Assert.Equal(1, result.RestorationsBlocked);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.OwnershipLost, state.State);
        Assert.Equal(ArtworkOperationPhase.Aborted, _operations.Read(_item, Surface).Value!.Phase);

        // The retained source artifact and the recovery records survive an
        // incomplete uninstall; ArrTags deletes nothing.
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_sourceArtifactId).Status);
    }

    [Fact]
    public async Task MissingRetainedSourceBlocksRestorationWithoutAnyImageCall()
    {
        var missing = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("missing-source"));
        var capture = new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            missing,
            10,
            100,
            150,
            At,
            "tag-1");
        var artifact = new SourceArtifactInfo(missing, "image/png", 10, missing);
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, _item, Surface, capture, artifact, At)
            .State;
        var published = PublishedArtworkStateTransitions
            .CommitPublication(session, Identity(_derived), Fingerprint(_derived), 2, At)
            .State;
        _states.Write(published);
        _host.CurrentBytes = _derived;

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Disable, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Incomplete, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
        Assert.Equal(ArtworkPublicationState.RestoreBlocked, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(StateReadStatus.Missing, _operations.Read(_item, Surface).Status);
    }

    [Fact]
    public async Task UnverifiableRestorationResultIsRecoveryBlockedAndRetained()
    {
        _states.Write(PublishedPresentState());
        _host.CurrentBytes = _derived;
        _host.SaveAppliesBytes = false;

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Incomplete, result.Outcome);
        Assert.Equal(1, result.RestorationsBlocked);
        Assert.Equal(1, _host.SaveCalls);

        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.RecoveryBlocked, operation.Phase);
        Assert.Equal(ArtworkPublicationState.RestoreBlocked, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_sourceArtifactId).Status);
    }

    [Fact]
    public async Task DrainReconcilesANonTerminalOperationBeforeRestoring()
    {
        _states.Write(PublishedPresentState());
        _operations.Write(PublicationOperation(ArtworkOperationPhase.Prepared, Identity(_derived), _external));
        _host.CurrentBytes = _derived;

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.OperationsReconciled);
        Assert.Equal(1, result.RestorationsCompleted);

        // The in-flight publication was reconciled and then replaced by a
        // committed restoration operation for the same subject.
        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationKind.Restoration, operation.Kind);
        Assert.Equal(2, operation.Generation);
        Assert.Equal(ArtworkOperationPhase.Committed, operation.Phase);
        Assert.Equal(ArtworkPublicationState.Restored, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(_source, _host.SavedBytes);
    }

    [Fact]
    public async Task DrainIsCancelledWhenTheTokenIsAlreadyCancelled()
    {
        _states.Write(PublishedPresentState());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, source.Token);

        Assert.Equal(ArtworkLifecycleOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
    }

    [Fact]
    public async Task ShutdownDrainIsANoOpWhenTheHostFenceIsNormal()
    {
        _states.Write(PublishedPresentState());
        _host.CurrentBytes = _derived;
        _pluginLifecycle.Fence = ArtworkLifecycleFence.Normal;

        var result = await _coordinator.DrainForHostShutdownAsync(CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.NothingToDo, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(ArtworkPublicationState.Published, _states.Read(_item, Surface).Value!.State);
    }

    [Fact]
    public async Task ShutdownDrainUsesTheHostStatusWhenTheDurableFenceIsNormal()
    {
        _states.Write(PublishedPresentState());
        _host.CurrentBytes = _derived;
        _pluginLifecycle.Fence = ArtworkLifecycleFence.Disable;

        var result = await _coordinator.DrainForHostShutdownAsync(CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Completed, result.Outcome);
        Assert.Equal(ArtworkLifecycleFence.Disable, _fences.Read().Fence);
        Assert.Equal(ArtworkPublicationState.Restored, _states.Read(_item, Surface).Value!.State);
    }

    [Fact]
    public async Task DrainContainsAThrowingReaderWithoutEscaping()
    {
        _states.Write(PublishedPresentState());
        _host.ThrowOnRead = true;

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Incomplete, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
    }

    [Fact]
    public async Task DrainReportsIncompleteWhenAReadFailureMakesAnOperationRecoveryBlocked()
    {
        _operations.Write(PublicationOperation(ArtworkOperationPhase.Prepared, Identity(_source), _derived));
        _host.ReadOverride = surface => ArtworkSourceReadResult.Failed(
            surface,
            ArtworkSourceReadFailureReason.Unreadable,
            "The active image could not be observed.");

        var result = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);

        Assert.Equal(ArtworkLifecycleOutcome.Incomplete, result.Outcome);
        Assert.Equal(0, result.OperationsReconciled);
        Assert.Equal(ArtworkOperationPhase.RecoveryBlocked, _operations.Read(_item, Surface).Value!.Phase);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
    }

    // ---- Item removal ----------------------------------------------------------

    [Fact]
    public async Task ConfirmedItemRemovalTombstonesTheOperationWithNoImageCalls()
    {
        _states.Write(PublishedPresentState());
        _operations.Write(PublicationOperation(ArtworkOperationPhase.MutationStarted, Identity(_derived), _external));
        _host.ReadOverride = surface => ArtworkSourceReadResult.Failed(
            surface,
            ArtworkSourceReadFailureReason.ItemNotFound,
            "The Jellyfin item was not found.");

        var result = await _coordinator.HandleItemRemovedAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRemovalOutcome.Confirmed, result.Outcome);
        Assert.True(result.Tombstoned);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
        Assert.Equal(0, _host.UpdateCalls);

        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Aborted, operation.Phase);
        Assert.Equal(ArtworkLifecycleFence.ItemRemoved, operation.LifecycleFence);
        Assert.Equal(ArtworkPublicationState.Removed, _states.Read(_item, Surface).Value!.State);

        // Only plugin-owned records exist; no artifact was deleted eagerly.
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_sourceArtifactId).Status);
    }

    [Fact]
    public async Task ConfirmedItemRemovalWithAnInvalidStateDoesNotClaimATombstone()
    {
        _operations.Write(PublicationOperation(ArtworkOperationPhase.MutationStarted, Identity(_derived), _external));
        _states.Write(PublishedPresentState());
        var statePath = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            PublishedArtworkStateStore.GetRecordId(_item, Surface));
        await File.WriteAllTextAsync(statePath, "{ this is not valid json");
        _host.ReadOverride = surface => ArtworkSourceReadResult.Failed(
            surface,
            ArtworkSourceReadFailureReason.ItemNotFound,
            "The Jellyfin item was not found.");

        var result = await _coordinator.HandleItemRemovedAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRemovalOutcome.Blocked, result.Outcome);
        Assert.False(result.Tombstoned);

        // The reconciler could not reach the ItemRemoved decision, so the
        // in-flight operation remains and no image mutation is attempted.
        Assert.Equal(ArtworkOperationPhase.MutationStarted, _operations.Read(_item, Surface).Value!.Phase);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
        Assert.Equal(0, _host.UpdateCalls);
    }

    [Fact]
    public async Task ConfirmedItemRemovalMarksAPublishedStateRemovedWithoutAnOperation()
    {
        _states.Write(PublishedPresentState());
        _host.ReadOverride = surface => ArtworkSourceReadResult.Failed(
            surface,
            ArtworkSourceReadFailureReason.ItemNotFound,
            "The Jellyfin item was not found.");

        var result = await _coordinator.HandleItemRemovedAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRemovalOutcome.Confirmed, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(ArtworkPublicationState.Removed, _states.Read(_item, Surface).Value!.State);
    }

    [Fact]
    public async Task UnconfirmedItemRemovalChangesNothing()
    {
        _states.Write(PublishedPresentState());
        _host.CurrentBytes = _derived;

        var result = await _coordinator.HandleItemRemovedAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRemovalOutcome.NotConfirmed, result.Outcome);
        Assert.False(result.Tombstoned);
        Assert.Equal(ArtworkPublicationState.Published, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.RemoveCalls);
    }

    [Fact]
    public async Task ItemRemovalConfirmationFailureIsBoundedAndChangesNothing()
    {
        _states.Write(PublishedPresentState());
        _host.ThrowOnRead = true;

        var result = await _coordinator.HandleItemRemovedAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRemovalOutcome.Blocked, result.Outcome);
        Assert.Equal(ArtworkPublicationState.Published, _states.Read(_item, Surface).Value!.State);
    }

    // ---- Host lifecycle status -------------------------------------------------

    [Fact]
    public void JellyfinPluginStatusMapsToTheBoundaryFence()
    {
        Assert.Equal(
            ArtworkLifecycleFence.Normal,
            new JellyfinPluginLifecycleState(CreatePluginManager(PluginStatus.Active)).GetLifecycleFence());
        Assert.Equal(
            ArtworkLifecycleFence.Disable,
            new JellyfinPluginLifecycleState(CreatePluginManager(PluginStatus.Disabled)).GetLifecycleFence());
        Assert.Equal(
            ArtworkLifecycleFence.Uninstall,
            new JellyfinPluginLifecycleState(CreatePluginManager(PluginStatus.Deleted)).GetLifecycleFence());
        Assert.Equal(
            ArtworkLifecycleFence.Normal,
            new JellyfinPluginLifecycleState(null).GetLifecycleFence());
    }

    // ---- Registration and boundary hygiene -------------------------------------

    [Fact]
    public void LifecycleServicesAreRegisteredWithoutStartupWork()
    {
        var services = new ServiceCollection();
        new ArrTagsServiceRegistrator().RegisterServices(services, null!);

        Assert.NotNull(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ArtworkLifecycleFenceStore)).ImplementationFactory);
        Assert.NotNull(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ArtworkLifecycleCoordinator)).ImplementationFactory);
        Assert.NotNull(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IArtworkLifecycleCoordinator)).ImplementationFactory);
        Assert.NotNull(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPluginLifecycleFenceProvider)).ImplementationFactory);
    }

    [Fact]
    public void LifecycleBoundaryExposesNoJellyfinTypesOrPaths()
    {
        AssertNoHostTypeLeak(typeof(ArtworkLifecycleCoordinator));
        AssertNoHostTypeLeak(typeof(ArtworkLifecycleResult));
        AssertNoHostTypeLeak(typeof(ArtworkLifecycleFenceStore));
        AssertNoHostTypeLeak(typeof(IArtworkLifecycleCoordinator));
        AssertNoHostTypeLeak(typeof(ArtworkRemovalResult));
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

    private PublishedArtworkState PublishedPresentState()
    {
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, _item, Surface, Identity(_source), ArtworkStateFixtures.Artifact(_source), At)
            .State;
        return PublishedArtworkStateTransitions
            .CommitPublication(session, Identity(_derived), Fingerprint(_derived), 2, At)
            .State;
    }

    private PublishedArtworkState PublishedAbsentState()
    {
        var absent = new ActiveImageIdentity(Surface, ArtworkImagePresence.Absent);
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, _item, Surface, absent, null, At)
            .State;
        return PublishedArtworkStateTransitions
            .CommitPublication(session, Identity(_derived), Fingerprint(_derived), 2, At)
            .State;
    }

    private ArtworkOperation PublicationOperation(
        ArtworkOperationPhase phase,
        ActiveImageIdentity before,
        byte[] candidate)
    {
        return new ArtworkOperation(
            ArtworkTokens.Create(),
            ArtworkOperationKind.Publication,
            _item,
            Surface,
            1,
            before,
            ArtworkImagePresence.Present,
            ArtworkImagePresence.Present,
            phase,
            ArtworkLifecycleFence.Normal,
            At,
            At,
            ownershipToken: ArtworkTokens.Create(),
            priorPublicationToken: ArtworkTokens.Create(),
            publicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: ArtworkHashes.ComputeSha256(candidate),
            sourceArtifactId: _sourceArtifactId,
            derivedArtifactId: _derivedArtifactId,
            candidatePublicationFingerprint: Fingerprint(candidate),
            rendererVersion: 2);
    }

    private ArtworkPublicationRequest Request(byte[] derived)
    {
        return new ArtworkPublicationRequest(
            _item,
            Surface,
            RenderResult.Rendered(
                derived,
                100,
                150,
                ArtworkHashes.ComputeSha256(derived),
                Fingerprint(derived)));
    }

    private static ActiveImageIdentity Identity(byte[] bytes, string tag = "tag-1")
    {
        return new ActiveImageIdentity(
            Surface,
            ArtworkImagePresence.Present,
            ArtworkHashes.ComputeSha256(bytes),
            bytes.Length,
            100,
            150,
            DateTimeOffset.UnixEpoch,
            tag);
    }

    private static string Fingerprint(byte[] bytes)
    {
        return ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("fingerprint-" + ArtworkHashes.ComputeSha256(bytes)));
    }

    private string Promote(byte[] bytes)
    {
        var promoted = _artifacts.Promote(bytes, "image/png", ArtworkHashes.ComputeSha256(bytes));
        Assert.True(promoted.Succeeded);
        return promoted.Info!.ArtifactId;
    }

    private static byte[] Png(byte marker)
    {
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, marker, marker];
    }

    private static IPluginManager CreatePluginManager(PluginStatus status)
    {
        var manifest = new PluginManifest { Status = status };
        var plugin = new LocalPlugin("ArrTags", true, manifest)
        {
            Instance = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin)),
        };
        var manager = DispatchProxy.Create<IPluginManager, FakePluginManager>();
        ((FakePluginManager)(object)manager).Plugins = [plugin];
        return manager;
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
    }

    private sealed class FakePluginLifecycleFenceProvider : IPluginLifecycleFenceProvider
    {
        public ArtworkLifecycleFence Fence { get; set; } = ArtworkLifecycleFence.Normal;

        public ArtworkLifecycleFence GetLifecycleFence()
        {
            return Fence;
        }
    }

    private sealed class FakeArtworkHost : IArtworkSourceReader, IArtworkImageWriter
    {
        public byte[]? CurrentBytes { get; set; }

        public bool SaveAppliesBytes { get; set; } = true;

        public bool ThrowOnRead { get; set; }

        public Func<ArtworkImageSurface, ArtworkSourceReadResult>? ReadOverride { get; set; }

        public int SaveCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public byte[]? SavedBytes { get; private set; }

        public string? SavedContentType { get; private set; }

        public Task<ArtworkSourceReadResult> ReadAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("The fake source read failed.");
            }

            if (ReadOverride is not null)
            {
                return Task.FromResult(ReadOverride(surface));
            }

            return Task.FromResult(Current(surface));
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
            SavedBytes = content.ToArray();
            SavedContentType = contentType;
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
            return Task.FromResult(ArtworkImageMutationResult.Success());
        }

        public Task<ArtworkImageMutationResult> RemoveImageAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveCalls++;
            CurrentBytes = null;
            return Task.FromResult(ArtworkImageMutationResult.Success());
        }

        private ArtworkSourceReadResult Current(ArtworkImageSurface surface)
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
    }

    public class FakePluginManager : DispatchProxy
    {
        public IReadOnlyList<LocalPlugin> Plugins { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_" + nameof(IPluginManager.Plugins))
            {
                return Plugins;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
