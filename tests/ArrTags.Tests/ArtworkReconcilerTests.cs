using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Rendering;
using ArrTags.State;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.7 focused checks for the provider-neutral postcondition
/// reconciliation service. The host source reader and image writer are replaced
/// with injectable doubles, so these tests run without a live Jellyfin host, do
/// not mutate a real library, and exercise every branch of the data-model 3.10.4
/// decision table, the lifecycle-fence gate, source retention, cleanup safety,
/// corruption handling, the item-removal tombstone, and the durable-final-state
/// completion. No exception escapes the reconciliation boundary.
/// </summary>
public sealed class ArtworkReconcilerTests : IDisposable
{
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(5);

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly SourceArtifactStore _artifacts;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly FakeArtworkHost _host;
    private readonly ArtworkPublisher _publisher;
    private readonly ArtworkReconciler _reconciler;
    private readonly Guid _item = Guid.NewGuid();
    private readonly byte[] _source;
    private readonly byte[] _derived;
    private readonly byte[] _external;
    private readonly string _sourceArtifactId;
    private readonly string _derivedArtifactId;

    public ArtworkReconcilerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-reconcile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        _host = new FakeArtworkHost();
        _publisher = new ArtworkPublisher(_host, _host, _artifacts, _states, _operations, new OperationalLimits());
        _reconciler = new ArtworkReconciler(_host, _states, _operations, _publisher);

        _source = Png(0x11);
        _derived = Png(0x22);
        _external = Png(0x33);
        _sourceArtifactId = Promote(_source);
        _derivedArtifactId = Promote(_derived);
        _host.CurrentBytes = _source;
    }

    // ---- Prepared and mutation-started before match ---------------------------

    [Fact]
    public async Task PreparedBeforeMatchResumesAndCommitsWithoutRecapturing()
    {
        var operation = Publication(ArtworkOperationPhase.Prepared);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Resumed, result.Outcome);
        Assert.True(result.IsCommitted);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);
        Assert.Equal(_derived, _host.SavedBytes);

        var durable = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Committed, durable.Phase);
        Assert.Equal(operation.OperationId, durable.OperationId);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, state.State);
        Assert.Equal(_sourceArtifactId, state.SourceArtifactId);
        Assert.Equal(ArtworkHashes.ComputeSha256(_source), state.SourceFingerprint);
        Assert.Equal(ArtworkHashes.ComputeSha256(_derived), state.ActiveImageIdentity!.ContentSha256);
    }

    [Fact]
    public async Task MutationStartedBeforeMatchRetriesTheSameDeterministicOperation()
    {
        var operation = Publication(ArtworkOperationPhase.MutationStarted);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Resumed, result.Outcome);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(_derived, _host.SavedBytes);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(_sourceArtifactId, state.SourceArtifactId);
        Assert.Equal(_sourceArtifactId, _artifacts.Read(state.SourceArtifactId!).Info!.ArtifactId);
    }

    [Fact]
    public async Task RepeatPublicationRecoveryReusesTheSourceArtifactAndOwnershipToken()
    {
        var first = PublishedState();
        _states.Write(first);
        var secondDerived = Png(0x44);
        var secondArtifact = Promote(secondDerived);
        var operation = Publication(
            ArtworkOperationPhase.Prepared,
            before: Identity(_derived),
            candidate: secondDerived,
            derivedArtifactId: secondArtifact,
            sourceArtifactId: first.SourceArtifactId,
            ownershipToken: first.OwnershipToken);
        _operations.Write(operation);
        _host.CurrentBytes = _derived;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Resumed, result.Outcome);
        Assert.Equal(1, _host.SaveCalls);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(first.OwnershipToken, state.OwnershipToken);
        Assert.Equal(first.SourceArtifactId, state.SourceArtifactId);
        Assert.NotEqual(first.PublicationToken, state.PublicationToken);
        Assert.Equal(ArtworkHashes.ComputeSha256(secondDerived), state.ActiveImageIdentity!.ContentSha256);
        Assert.Equal(_sourceArtifactId, _artifacts.Read(state.SourceArtifactId!).Info!.ArtifactId);
    }

    // ---- After match ----------------------------------------------------------

    [Fact]
    public async Task CandidateAfterMatchEnsuresItemUpdateAndCommits()
    {
        var operation = Publication(ArtworkOperationPhase.VerificationPending);
        _operations.Write(operation);
        _host.CurrentBytes = _derived;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);

        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkPublicationState.Published, state.State);
        Assert.Equal(ArtworkHashes.ComputeSha256(_derived), state.ActiveImageIdentity!.ContentSha256);
        Assert.Equal(ArtworkOperationPhase.Committed, _operations.Read(_item, Surface).Value!.Phase);
    }

    [Fact]
    public async Task ObservedAfterIdentityMatchCommits()
    {
        var operation = WithObservedAfter(
            Publication(ArtworkOperationPhase.FinalizationPending),
            Identity(_derived));
        _operations.Write(operation);
        _host.CurrentBytes = _derived;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);
        Assert.Equal(ArtworkPublicationState.Published, _states.Read(_item, Surface).Value!.State);
    }

    // ---- Final state durable --------------------------------------------------

    [Fact]
    public async Task DurableFinalStateCompletesTheJournalWithoutMutation()
    {
        var operation = Publication(ArtworkOperationPhase.VerificationPending);
        _operations.Write(operation);
        _states.Write(PublishedState(operation.OperationId));
        _host.CurrentBytes = _derived;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.CommittedFinalState, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkOperationPhase.Committed, _operations.Read(_item, Surface).Value!.Phase);
        Assert.Equal(ArtworkPublicationState.Published, _states.Read(_item, Surface).Value!.State);
    }

    // ---- Ownership loss and uncertainty ---------------------------------------

    [Fact]
    public async Task ExternalChangeRecordsOwnershipLostAndAbortsWithoutMutation()
    {
        var operation = Publication(ArtworkOperationPhase.MutationStarted);
        _operations.Write(operation);
        _states.Write(PublishedState());
        _host.CurrentBytes = _external;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.OwnershipLost, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkPublicationState.OwnershipLost, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(ArtworkOperationPhase.Aborted, _operations.Read(_item, Surface).Value!.Phase);
    }

    [Fact]
    public async Task UnobservableIdentityRecordsOwnershipUnknownAndLeavesTheImageUntouched()
    {
        var operation = Publication(ArtworkOperationPhase.MutationStarted);
        _operations.Write(operation);
        _states.Write(PublishedState());
        _host.ReadOverride = surface => ArtworkSourceReadResult.Failed(
            surface,
            ArtworkSourceReadFailureReason.Unreadable,
            "unobservable");

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.OwnershipUnknown, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkPublicationState.OwnershipUnknown, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(ArtworkOperationPhase.RecoveryBlocked, _operations.Read(_item, Surface).Value!.Phase);
    }

    // ---- Item removal ---------------------------------------------------------

    [Fact]
    public async Task ItemAbsentWritesARemovalTombstoneAndPerformsNoMutation()
    {
        var operation = Publication(ArtworkOperationPhase.MutationStarted);
        _operations.Write(operation);
        _states.Write(PublishedState());
        _host.ReadOverride = surface => ArtworkSourceReadResult.Failed(
            surface,
            ArtworkSourceReadFailureReason.ItemNotFound,
            "The Jellyfin item was not found.");

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.ItemRemoved, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);

        var tombstone = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Aborted, tombstone.Phase);
        Assert.Equal(ArtworkLifecycleFence.ItemRemoved, tombstone.LifecycleFence);
        Assert.Equal(ArtworkPublicationState.Removed, _states.Read(_item, Surface).Value!.State);

        // Cleanup records and artifacts are retained rather than deleted.
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_sourceArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_derivedArtifactId).Status);
    }

    // ---- Lifecycle fence ------------------------------------------------------

    [Fact]
    public async Task LifecycleFenceAbortsAPreparedPublicationWithoutMutation()
    {
        var operation = Publication(ArtworkOperationPhase.Prepared, fence: ArtworkLifecycleFence.Disable);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Aborted, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkOperationPhase.Aborted, _operations.Read(_item, Surface).Value!.Phase);
    }

    // ---- Corruption and artifact safety ---------------------------------------

    [Fact]
    public async Task MissingDerivedArtifactOnResumeEntersRecoveryBlockedWithoutMutation()
    {
        var missing = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("missing-derived"));
        var operation = Publication(ArtworkOperationPhase.Prepared, derivedArtifactId: missing);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.RecoveryBlocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkOperationPhase.RecoveryBlocked, _operations.Read(_item, Surface).Value!.Phase);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_sourceArtifactId).Status);
    }

    [Fact]
    public async Task MissingSourceArtifactOnResumeEntersRecoveryBlocked()
    {
        var missing = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("missing-source"));
        var operation = Publication(ArtworkOperationPhase.MutationStarted, sourceArtifactId: missing);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.RecoveryBlocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task CorruptStateRecordIsQuarantinedAndBlocksRecovery()
    {
        _operations.Write(Publication(ArtworkOperationPhase.Prepared));
        _states.Write(PublishedState());
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            PublishedArtworkStateStore.GetRecordId(_item, Surface));
        await File.WriteAllTextAsync(path, "{ this is not valid json");

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.RecoveryBlocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(ArtworkOperationPhase.Prepared, _operations.Read(_item, Surface).Value!.Phase);
    }

    [Fact]
    public async Task CorruptOperationRecordIsQuarantinedAndBlocksRecovery()
    {
        _operations.Write(Publication(ArtworkOperationPhase.Prepared));
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkOperationStore.RecordKind,
            ArtworkOperationStore.GetRecordId(_item, Surface));
        await File.WriteAllTextAsync(path, "{ this is not valid json");

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.RecoveryBlocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task RecoveryBlockedOperationIsResumedByALaterReconciliation()
    {
        var operation = Publication(ArtworkOperationPhase.RecoveryBlocked);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Resumed, result.Outcome);
        Assert.Equal(1, _host.SaveCalls);

        var durable = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.Committed, durable.Phase);
        Assert.Equal(operation.Generation + 1, durable.Generation);
        Assert.Equal(operation.OperationId, durable.OperationId);
    }

    [Fact]
    public async Task RecoveryBlockedOperationWithAfterMatchCompletes()
    {
        var operation = Publication(ArtworkOperationPhase.RecoveryBlocked);
        _operations.Write(operation);
        _host.CurrentBytes = _derived;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);
        Assert.Equal(ArtworkPublicationState.Published, _states.Read(_item, Surface).Value!.State);
    }

    [Fact]
    public async Task BeforeIdentityChangedBetweenObservationAndResumeAbortsWithoutMutation()
    {
        var operation = Publication(ArtworkOperationPhase.Prepared);
        _operations.Write(operation);
        var reads = 0;
        _host.ReadHandler = _ => reads++ == 0 ? CurrentResult(_source) : CurrentResult(_external);

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.OwnershipLost, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkOperationPhase.Aborted, _operations.Read(_item, Surface).Value!.Phase);
    }

    [Fact]
    public async Task RestorationResumeIsDeferredToLifecycleHandling()
    {
        var sourceHash = _sourceArtifactId;
        var operation = new ArtworkOperation(
            ArtworkTokens.Create(),
            ArtworkOperationKind.Restoration,
            _item,
            Surface,
            1,
            Identity(_derived),
            ArtworkImagePresence.Present,
            ArtworkImagePresence.Present,
            ArtworkOperationPhase.Prepared,
            ArtworkLifecycleFence.Disable,
            At,
            At,
            ownershipToken: ArtworkTokens.Create(),
            priorPublicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: sourceHash,
            sourceArtifactId: sourceHash);
        _operations.Write(operation);
        _states.Write(RestorePendingState());
        _host.CurrentBytes = _derived;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.Deferred, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkOperationPhase.Prepared, _operations.Read(_item, Surface).Value!.Phase);
    }

    // ---- No work and bounded failure -------------------------------------------

    [Fact]
    public async Task NoOperationIsNothingToReconcile()
    {
        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.NothingToReconcile, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task CommittedOperationIsNotReconciled()
    {
        _operations.Write(Publication(ArtworkOperationPhase.Committed));

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.NothingToReconcile, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task CancellationDoesNotMutate()
    {
        _operations.Write(Publication(ArtworkOperationPhase.Prepared));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var result = await _reconciler.ReconcileAsync(_item, Surface, source.Token);

        Assert.Equal(ArtworkReconciliationOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
    }

    [Fact]
    public async Task SourceReaderExceptionIsContainedAndBlocksSafely()
    {
        _operations.Write(Publication(ArtworkOperationPhase.Prepared));
        _host.ThrowOnRead = true;

        var result = await _reconciler.ReconcileAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkReconciliationOutcome.RecoveryBlocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public void ReconcilerIsRegisteredWithoutStartupWork()
    {
        var services = new ServiceCollection();
        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        var reconciler = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ArtworkReconciler));
        Assert.NotNull(reconciler.ImplementationFactory);
    }

    [Fact]
    public void ReconciliationBoundaryExposesNoJellyfinTypesOrPaths()
    {
        AssertNoHostTypeLeak(typeof(ArtworkReconciler));
        AssertNoHostTypeLeak(typeof(ArtworkReconciliationResult));
        AssertNoHostTypeLeak(typeof(ArtworkRecoveryDecision));
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

    private ArtworkOperation Publication(
        ArtworkOperationPhase phase,
        ArtworkLifecycleFence fence = ArtworkLifecycleFence.Normal,
        string? sourceArtifactId = null,
        string? derivedArtifactId = null,
        ActiveImageIdentity? before = null,
        byte[]? candidate = null,
        string? ownershipToken = null)
    {
        var candidateBytes = candidate ?? _derived;
        return new ArtworkOperation(
            ArtworkTokens.Create(),
            ArtworkOperationKind.Publication,
            _item,
            Surface,
            1,
            before ?? Identity(_source),
            ArtworkImagePresence.Present,
            ArtworkImagePresence.Present,
            phase,
            fence,
            At,
            At,
            ownershipToken: ownershipToken ?? ArtworkTokens.Create(),
            priorPublicationToken: ArtworkTokens.Create(),
            publicationToken: ArtworkTokens.Create(),
            candidateAfterContentSha256: ArtworkHashes.ComputeSha256(candidateBytes),
            sourceArtifactId: sourceArtifactId ?? _sourceArtifactId,
            derivedArtifactId: derivedArtifactId ?? _derivedArtifactId,
            candidatePublicationFingerprint: ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("publication-fingerprint")),
            rendererVersion: RenderVersion.CurrentRendererVersion);
    }

    private static ArtworkOperation WithObservedAfter(ArtworkOperation operation, ActiveImageIdentity observedAfter)
    {
        return new ArtworkOperation(
            operation.OperationId,
            operation.Kind,
            operation.JellyfinItemId,
            operation.ImageSurface,
            operation.Generation,
            operation.ExpectedBeforeIdentity,
            operation.SourcePresence,
            operation.CandidateAfterPresence,
            operation.Phase,
            operation.LifecycleFence,
            operation.CreatedAt,
            operation.UpdatedAt,
            operation.ModelVersion,
            operation.OwnershipToken,
            operation.PriorPublicationToken,
            operation.PublicationToken,
            operation.CandidateAfterContentSha256,
            observedAfter,
            operation.SourceArtifactId,
            operation.DerivedArtifactId,
            operation.Attempt,
            operation.LastError,
            operation.CandidatePublicationFingerprint,
            operation.RendererVersion);
    }

    private PublishedArtworkState PublishedState(string? operationId = null)
    {
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, _item, Surface, Identity(_source), ArtworkStateFixtures.Artifact(_source), At)
            .State;
        var committed = PublishedArtworkStateTransitions
            .CommitPublication(
                session,
                Identity(_derived, "active-tag"),
                ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("published-fingerprint")),
                RenderVersion.CurrentRendererVersion,
                At)
            .State;

        if (operationId is null)
        {
            return committed;
        }

        return new PublishedArtworkState(
            committed.JellyfinItemId,
            committed.ImageSurface,
            committed.State,
            committed.UpdatedAt,
            committed.ModelVersion,
            committed.StateRevision,
            committed.SourcePresence,
            committed.SourceArtifactId,
            committed.SourceFingerprint,
            committed.SourceCaptureIdentity,
            committed.OwnershipToken,
            committed.PublicationToken,
            committed.ActiveImageIdentity,
            committed.PublishedFingerprint,
            committed.RendererVersion,
            committed.LastOwnershipObservation,
            operationId);
    }

    private PublishedArtworkState RestorePendingState()
    {
        return PublishedArtworkStateTransitions.RequestRestore(PublishedState(), At.AddDays(1)).State;
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

    private static ArtworkSourceReadResult CurrentResult(byte[] bytes)
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

    private sealed class FakeArtworkHost : IArtworkSourceReader, IArtworkImageWriter
    {
        public byte[]? CurrentBytes { get; set; }

        public bool ApplySave { get; set; } = true;

        public bool ThrowOnRead { get; set; }

        public Func<ArtworkImageSurface, ArtworkSourceReadResult>? ReadOverride { get; set; }

        public Func<int, ArtworkSourceReadResult>? ReadHandler { get; set; }

        private int _readCount;

        public int SaveCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public byte[]? SavedBytes { get; private set; }

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

            if (ReadHandler is not null)
            {
                return Task.FromResult(ReadHandler(_readCount++));
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
            if (ApplySave)
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
}
