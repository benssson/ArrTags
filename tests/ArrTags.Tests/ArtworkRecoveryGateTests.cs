using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.PluginLifecycle;
using ArrTags.Rendering;
using ArrTags.State;
using ArrTags.Updates;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 6 task 6.4 focused checks for recovering non-terminal artwork operations
/// before new work is accepted, the interleaved lifecycle drain/publication race,
/// the generation and lifecycle fences, fail-closed corruption handling, and the
/// bounded startup scan. The host source reader and image writer are injectable
/// doubles, so these tests run without a live Jellyfin host and mutate no real
/// library.
/// </summary>
public sealed class ArtworkRecoveryGateTests : IDisposable
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
    private readonly ArtworkPublisher _publisher;
    private readonly ArtworkReconciler _reconciler;
    private readonly ArtworkLifecycleCoordinator _coordinator;
    private readonly ArtworkRecoveryGate _gate;
    private readonly Guid _item = Guid.NewGuid();
    private readonly byte[] _source;
    private readonly byte[] _derived;
    private readonly byte[] _external;
    private readonly string _sourceArtifactId;
    private readonly string _derivedArtifactId;

    public ArtworkRecoveryGateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-recovery-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _artifacts = new SourceArtifactStore(_repository);
        _states = new PublishedArtworkStateStore(_repository);
        _operations = new ArtworkOperationStore(_repository);
        _fences = new ArtworkLifecycleFenceStore(_repository);
        _host = new FakeArtworkHost();
        _publisher = new ArtworkPublisher(_host, _host, _artifacts, _states, _operations, new OperationalLimits(), _fences);
        _reconciler = new ArtworkReconciler(_host, _states, _operations, _publisher);
        _coordinator = new ArtworkLifecycleCoordinator(
            _host,
            _states,
            _operations,
            _reconciler,
            _publisher,
            _fences,
            new FakePluginLifecycleFenceProvider());
        _gate = new ArtworkRecoveryGate(_operations, _reconciler, _fences);

        _source = Png(0x11);
        _derived = Png(0x22);
        _external = Png(0x33);
        _sourceArtifactId = Promote(_source);
        _derivedArtifactId = Promote(_derived);
        _host.CurrentBytes = _source;
    }

    // ---- Per-subject recovery gate ---------------------------------------------

    [Fact]
    public async Task GateAllowsNewWorkWhenThereIsNoDurableOperation()
    {
        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRecoveryGateOutcome.NoOperation, result.Outcome);
        Assert.True(result.CanProceed);
        Assert.Equal(0, result.Generation);
        Assert.Null(result.OperationId);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task GateAllowsNewWorkWhenTheOperationIsAlreadyTerminal()
    {
        var operation = PublicationOperation(ArtworkOperationPhase.Committed);
        _operations.Write(operation);

        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRecoveryGateOutcome.AlreadyTerminal, result.Outcome);
        Assert.True(result.CanProceed);
        Assert.Equal(operation.Generation, result.Generation);
        Assert.Equal(operation.OperationId, result.OperationId);
        Assert.Equal(0, _host.SaveCalls);
    }

    [Fact]
    public async Task GateRecoversANonTerminalPublicationBeforeAllowingNewWork()
    {
        var operation = PublicationOperation(ArtworkOperationPhase.Prepared);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRecoveryGateOutcome.Recovered, result.Outcome);
        Assert.True(result.CanProceed);
        Assert.Equal(operation.Generation, result.Generation);
        Assert.Equal(operation.OperationId, result.OperationId);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(_derived, _host.SavedBytes);
        Assert.Equal(ArtworkOperationPhase.Committed, _operations.Read(_item, Surface).Value!.Phase);
        Assert.Equal(ArtworkPublicationState.Published, _states.Read(_item, Surface).Value!.State);
    }

    [Fact]
    public async Task GateBlocksNewWorkWhenTheOperationRecordIsCorrupt()
    {
        _operations.Write(PublicationOperation(ArtworkOperationPhase.Prepared));
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkOperationStore.RecordKind,
            ArtworkOperationStore.GetRecordId(_item, Surface));
        await File.WriteAllTextAsync(path, "{ this is not valid json");

        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRecoveryGateOutcome.Blocked, result.Outcome);
        Assert.False(result.CanProceed);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
    }

    [Fact]
    public async Task GateFailClosedRecoveryRetainsArtifactsAndMutatesNothing()
    {
        var missing = ArtworkHashes.ComputeSha256(Encoding.UTF8.GetBytes("missing-derived"));
        var operation = PublicationOperation(ArtworkOperationPhase.Prepared, derivedArtifactId: missing);
        _operations.Write(operation);
        _host.CurrentBytes = _source;

        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        // The reconciler reached its durable terminal RecoveryBlocked outcome
        // without any image mutation; the gate then permits later work, while the
        // publisher still refuses artwork publication for a RecoveryBlocked op.
        Assert.Equal(ArtworkRecoveryGateOutcome.Recovered, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(ArtworkOperationPhase.RecoveryBlocked, _operations.Read(_item, Surface).Value!.Phase);

        // Recovery never deletes an artifact that is not proven non-active.
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_sourceArtifactId).Status);
        Assert.Equal(SourceArtifactReadStatus.Found, _artifacts.Read(_derivedArtifactId).Status);

        var retry = await _publisher.PublishAsync(Request(Png(0x44)), CancellationToken.None);
        Assert.False(retry.Published);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, retry.Outcome);
    }

    [Fact]
    public async Task GateRecoveryLeavesAnExternallyChangedImageUntouched()
    {
        var operation = PublicationOperation(ArtworkOperationPhase.MutationStarted);
        _operations.Write(operation);
        _states.Write(PublishedState());
        _host.CurrentBytes = _external;

        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRecoveryGateOutcome.Recovered, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);
        Assert.Equal(ArtworkPublicationState.OwnershipLost, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(ArtworkOperationPhase.Aborted, _operations.Read(_item, Surface).Value!.Phase);
    }

    [Fact]
    public async Task GateRecoveryReusesTheRetainedSourceAndNeverRecapturesTheActiveImage()
    {
        var first = PublishedState();
        _states.Write(first);
        var secondDerived = Png(0x44);
        var secondArtifact = Promote(secondDerived);
        var operation = PublicationOperation(
            ArtworkOperationPhase.Prepared,
            before: Identity(_derived),
            candidate: secondDerived,
            derivedArtifactId: secondArtifact,
            sourceArtifactId: first.SourceArtifactId,
            ownershipToken: first.OwnershipToken);
        _operations.Write(operation);
        _host.CurrentBytes = _derived;

        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRecoveryGateOutcome.Recovered, result.Outcome);
        Assert.Equal(1, _host.SaveCalls);

        // The repeat publication reuses the retained original source; the current
        // ArrTags-derived active image was never captured as the new source.
        var state = _states.Read(_item, Surface).Value!;
        Assert.Equal(first.OwnershipToken, state.OwnershipToken);
        Assert.Equal(_sourceArtifactId, state.SourceArtifactId);
        Assert.Equal(_source, _artifacts.Read(state.SourceArtifactId!).Bytes.ToArray());
        Assert.Equal(ArtworkHashes.ComputeSha256(secondDerived), state.ActiveImageIdentity!.ContentSha256);
    }

    [Fact]
    public async Task GateBindsAcceptedWorkToTheCurrentGenerationAndRejectsStaleWrites()
    {
        _operations.Write(PublicationOperation(ArtworkOperationPhase.Committed, generation: 4));

        var result = await _gate.EnsureRecoveredAsync(_item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkRecoveryGateOutcome.AlreadyTerminal, result.Outcome);
        Assert.Equal(4, result.Generation);

        // New work must supersede the durable generation; a stale generation can
        // never overwrite the newer durable record.
        Assert.True(ArtworkOperationFencing.CanSupersede(result.Generation, result.Generation + 1));
        Assert.True(ArtworkOperationFencing.IsStale(result.Generation, result.Generation - 1));
        Assert.Throws<InvalidOperationException>(() => _operations.Write(
            PublicationOperation(ArtworkOperationPhase.Prepared, generation: 3)));
    }

    // ---- Startup scan bounds ----------------------------------------------------

    [Fact]
    public async Task StartupScanIsBoundedToTheBatchSize()
    {
        for (var index = 0; index < 5; index++)
        {
            _operations.Write(PublicationOperation(ArtworkOperationPhase.Prepared, item: Guid.NewGuid()));
        }

        var result = await _gate.RecoverStartupAsync(2, CancellationToken.None);

        Assert.Equal(2, result.Examined);
        Assert.Equal(2, result.Recovered);
        Assert.True(result.ReachedBatchLimit);

        var remaining = _operations.Enumerate(10).Where(operation => !operation.IsTerminal).ToList();
        Assert.Equal(3, remaining.Count);
        Assert.All(remaining, operation => Assert.Equal(ArtworkOperationPhase.Prepared, operation.Phase));
    }

    [Fact]
    public async Task StartupScanRecoversNonTerminalOperationsAndReportsTerminalOnes()
    {
        _operations.Write(PublicationOperation(ArtworkOperationPhase.Prepared, item: Guid.NewGuid()));
        _operations.Write(PublicationOperation(ArtworkOperationPhase.Committed, item: Guid.NewGuid()));

        var result = await _gate.RecoverStartupAsync(10, CancellationToken.None);

        Assert.Equal(2, result.Examined);
        Assert.Equal(1, result.Recovered);
        Assert.Equal(1, result.AlreadyTerminal);
        Assert.False(result.ReachedBatchLimit);
        Assert.False(result.Cancelled);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task StartupRecoveryServiceRunsTheConfiguredBatchAndStopsBounded()
    {
        var gate = new RecordingGate();
        var service = new ArtworkStartupRecoveryService(
            gate,
            new ConfigurationSnapshotService(),
            TimeSpan.FromSeconds(5));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.True(gate.Started);
        Assert.Equal(new OperationalLimits().ReconciliationBatchSize, gate.BatchSize);
        service.Dispose();
    }

    [Fact]
    public async Task StartupScanIsCancellableAndPerformsNoRecoveryWhenCancelled()
    {
        _operations.Write(PublicationOperation(ArtworkOperationPhase.Prepared));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var result = await _gate.RecoverStartupAsync(10, source.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Examined);
        Assert.Equal(0, _host.SaveCalls);
    }

    // ---- Interleaved lifecycle drain and publication race -----------------------

    [Fact]
    public async Task FenceRaisedAfterPreparationAbortsBeforeAnyMutation()
    {
        var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.PauseRead = async () =>
        {
            _host.ReadEntered.TrySetResult();
            await pause.Task.ConfigureAwait(false);
        };
        _host.CurrentBytes = _source;

        var publication = _publisher.PublishAsync(Request(_derived), CancellationToken.None);

        // The publication is paused in its initial source read, before it writes
        // any durable Prepared operation.
        await _host.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(StateReadStatus.Missing, _operations.Read(_item, Surface).Status);

        // The disable/uninstall drain runs to completion while the publication is
        // paused, so the drain cannot enumerate or reconcile the untracked work.
        var drain = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);
        Assert.Equal(ArtworkLifecycleOutcome.Completed, drain.Outcome);
        Assert.Equal(0, _host.SaveCalls);

        pause.SetResult();
        var result = await publication;

        // The pre-mutation fence check aborts the publication without touching the
        // image, so no untracked non-terminal publication survives the fence.
        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, result.Outcome);
        Assert.Equal(0, _host.SaveCalls);
        Assert.Equal(0, _host.UpdateCalls);

        var operation = _operations.Read(_item, Surface);
        Assert.True(
            operation.Status == StateReadStatus.Missing || operation.Value!.IsTerminal,
            "No non-terminal publication may survive the fence.");
        Assert.Equal(StateReadStatus.Missing, _states.Read(_item, Surface).Status);
    }

    [Fact]
    public async Task FenceRaisedDuringMutationPreventsTheFinalCommitAndLeavesARecoverableOperation()
    {
        _host.CurrentBytes = _source;
        _host.OnPersistItemUpdate = () => _fences.Set(
            ArtworkLifecycleFence.Uninstall,
            "The fence was raised while the publication was in flight.");

        var result = await _publisher.PublishAsync(Request(_derived), CancellationToken.None);

        // The mutation already happened, but the publication did not commit the
        // final state across the fence.
        Assert.False(result.Published);
        Assert.Equal(ArtworkPublicationOutcome.Blocked, result.Outcome);
        Assert.Equal(1, _host.SaveCalls);
        Assert.Equal(1, _host.UpdateCalls);
        Assert.Equal(StateReadStatus.Missing, _states.Read(_item, Surface).Status);

        var operation = _operations.Read(_item, Surface).Value!;
        Assert.Equal(ArtworkOperationPhase.FinalizationPending, operation.Phase);
        Assert.Equal(ArtworkHashes.ComputeSha256(_derived), operation.ObservedAfterIdentity!.ContentSha256);

        // Recovery reconciles the verified postcondition and the drain then
        // restores the original, so no untracked publication survives the fence.
        var drain = await _coordinator.DrainAsync(ArtworkLifecycleFence.Uninstall, CancellationToken.None);
        Assert.Equal(ArtworkLifecycleOutcome.Completed, drain.Outcome);
        Assert.Equal(ArtworkPublicationState.Restored, _states.Read(_item, Surface).Value!.State);
        Assert.Equal(_source, _host.CurrentBytes);
    }

    [Fact]
    public async Task GateDefersAndBlocksWorkWhenRecoveryCannotProceed()
    {
        var gate = new StubGate(ArtworkRecoveryGateOutcome.Deferred);
        var inner = new RecordingWorkProcessor(WorkProcessingResult.Completed());
        var processor = new ArtworkRecoveringWorkItemProcessor(inner, gate);

        var deferred = await processor.ProcessAsync(WorkItem(), CancellationToken.None);
        Assert.True(deferred.IsRetryable);
        Assert.Equal(0, inner.Calls);

        var blockedGate = new StubGate(ArtworkRecoveryGateOutcome.Blocked);
        var blockedProcessor = new ArtworkRecoveringWorkItemProcessor(inner, blockedGate);
        var blocked = await blockedProcessor.ProcessAsync(WorkItem(), CancellationToken.None);
        Assert.False(blocked.IsSuccess);
        Assert.False(blocked.IsRetryable);
        Assert.Equal(0, inner.Calls);

        var allowedGate = new StubGate(ArtworkRecoveryGateOutcome.Recovered);
        var allowedProcessor = new ArtworkRecoveringWorkItemProcessor(inner, allowedGate);
        var allowed = await allowedProcessor.ProcessAsync(WorkItem(), CancellationToken.None);
        Assert.True(allowed.IsSuccess);
        Assert.Equal(1, inner.Calls);
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

    private ArtworkOperation PublicationOperation(
        ArtworkOperationPhase phase,
        Guid? item = null,
        long generation = 1,
        ActiveImageIdentity? before = null,
        byte[]? candidate = null,
        string? sourceArtifactId = null,
        string? derivedArtifactId = null,
        string? ownershipToken = null,
        ArtworkLifecycleFence fence = ArtworkLifecycleFence.Normal)
    {
        var candidateBytes = candidate ?? _derived;
        var derived = derivedArtifactId ?? Promote(candidateBytes);
        return new ArtworkOperation(
            ArtworkTokens.Create(),
            ArtworkOperationKind.Publication,
            item ?? _item,
            Surface,
            generation,
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
            derivedArtifactId: derived,
            candidatePublicationFingerprint: Fingerprint(candidateBytes),
            rendererVersion: RenderVersion.CurrentRendererVersion);
    }

    private PublishedArtworkState PublishedState()
    {
        var session = PublishedArtworkStateTransitions
            .CaptureSession(null, _item, Surface, Identity(_source), ArtworkStateFixtures.Artifact(_source), At)
            .State;
        return PublishedArtworkStateTransitions
            .CommitPublication(
                session,
                Identity(_derived),
                Fingerprint(_derived),
                RenderVersion.CurrentRendererVersion,
                At)
            .State;
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

    private LibraryWorkItem WorkItem()
    {
        return new LibraryWorkItem(
            new WorkItemKey(_item, null, Surface),
            LibraryWorkReason.Updated,
            1);
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

    private sealed class FakePluginLifecycleFenceProvider : IPluginLifecycleFenceProvider
    {
        public ArtworkLifecycleFence GetLifecycleFence()
        {
            return ArtworkLifecycleFence.Normal;
        }
    }

    private sealed class StubGate : IArtworkRecoveryGate
    {
        private readonly ArtworkRecoveryGateOutcome _outcome;

        public StubGate(ArtworkRecoveryGateOutcome outcome)
        {
            _outcome = outcome;
        }

        public Task<ArtworkRecoveryGateResult> EnsureRecoveredAsync(
            Guid jellyfinItemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkRecoveryGateResult.Create(_outcome, "The stub recovery gate outcome."));
        }

        public Task<ArtworkRecoveryScanResult> RecoverStartupAsync(int batchSize, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ArtworkRecoveryScanResult(0, 0, 0, 0, 0, false, false));
        }
    }

    private sealed class RecordingGate : IArtworkRecoveryGate
    {
        public bool Started { get; private set; }

        public int BatchSize { get; private set; } = -1;

        public Task<ArtworkRecoveryGateResult> EnsureRecoveredAsync(
            Guid jellyfinItemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ArtworkRecoveryGateResult.Create(
                ArtworkRecoveryGateOutcome.NoOperation,
                "The recording gate outcome."));
        }

        public Task<ArtworkRecoveryScanResult> RecoverStartupAsync(int batchSize, CancellationToken cancellationToken)
        {
            Started = true;
            BatchSize = batchSize;
            return Task.FromResult(new ArtworkRecoveryScanResult(0, 0, 0, 0, 0, false, false));
        }
    }

    private sealed class RecordingWorkProcessor : IWorkItemProcessor
    {
        private readonly WorkProcessingResult _result;

        public RecordingWorkProcessor(WorkProcessingResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }

        public Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeArtworkHost : IArtworkSourceReader, IArtworkImageWriter
    {
        public byte[]? CurrentBytes { get; set; }

        public bool SaveAppliesBytes { get; set; } = true;

        public bool ThrowOnRead { get; set; }

        public Func<Task>? PauseRead { get; set; }

        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action? OnPersistItemUpdate { get; set; }

        public int SaveCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public byte[]? SavedBytes { get; private set; }

        public string? SavedContentType { get; private set; }

        public async Task<ArtworkSourceReadResult> ReadAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("The fake source read failed.");
            }

            if (PauseRead is { } pause)
            {
                PauseRead = null;
                await pause().ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Current(surface);
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
            if (OnPersistItemUpdate is { } callback)
            {
                OnPersistItemUpdate = null;
                callback();
            }

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
}
