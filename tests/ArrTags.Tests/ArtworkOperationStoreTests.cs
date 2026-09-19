using System;
using System.IO;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks that the durable <see cref="ArtworkOperation"/> persists
/// through the authoritative versioned state boundary: round-trip and restart
/// durability, generation fencing, one-operation-per-subject enforcement,
/// corruption quarantine, and terminal-only retention. No live Jellyfin or Arr
/// instance is required and no image mutation is performed.
/// </summary>
public sealed class ArtworkOperationStoreTests : IDisposable
{
    private static readonly Guid Item = ArtworkOperationFixtures.Item;
    private static readonly ArtworkImageSurface Surface = ArtworkOperationFixtures.Surface;

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly ArtworkOperationStore _store;

    public ArtworkOperationStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-state-operation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new ArtworkOperationStore(_repository);
    }

    [Fact]
    public void WriteAndReadRoundTripsThroughTheAuthoritativeBoundary()
    {
        var operation = ArtworkOperationFixtures.Publication();

        _store.Write(operation);
        var result = _store.Read(Item, Surface);

        Assert.Equal(StateReadStatus.Found, result.Status);
        var stored = result.Value!;
        Assert.Equal(operation.OperationId, stored.OperationId);
        Assert.Equal(operation.Generation, stored.Generation);
        Assert.Equal(operation.Kind, stored.Kind);
        Assert.Equal(operation.Phase, stored.Phase);
        Assert.Equal(operation.LifecycleFence, stored.LifecycleFence);
        Assert.Equal(operation.SourceArtifactId, stored.SourceArtifactId);
        Assert.Equal(operation.DerivedArtifactId, stored.DerivedArtifactId);
        Assert.Equal(operation.CandidateAfterContentSha256, stored.CandidateAfterContentSha256);
        Assert.Equal(operation.ExpectedBeforeIdentity.ContentSha256, stored.ExpectedBeforeIdentity.ContentSha256);
        Assert.True(stored.Validate(out var reason), reason);
    }

    [Fact]
    public void RecordSurvivesRepositoryReconstruction()
    {
        var operation = ArtworkOperationFixtures.Restoration();
        _store.Write(operation);

        var reconstructed = new ArtworkOperationStore(new StateRepository(_root));
        var result = reconstructed.Read(Item, Surface);

        Assert.Equal(StateReadStatus.Found, result.Status);
        Assert.Equal(operation.OperationId, result.Value!.OperationId);
        Assert.Equal(ArtworkOperationKind.Restoration, result.Value.Kind);
    }

    [Fact]
    public void StaleGenerationCannotOverwriteANewerRecord()
    {
        var newer = ArtworkOperationFixtures.Publication(generation: 2);
        _store.Write(newer);

        var stale = ArtworkOperationFixtures.Publication(generation: 1);

        Assert.Throws<InvalidOperationException>(() => _store.Write(stale));
        Assert.Equal(newer.OperationId, _store.Read(Item, Surface).Value!.OperationId);
    }

    [Fact]
    public void NewerGenerationSupersedesTheDurableRecord()
    {
        var first = ArtworkOperationFixtures.Publication(generation: 1);
        _store.Write(first);

        var second = ArtworkOperationFixtures.Publication(generation: 2);
        _store.Write(second);

        var stored = _store.Read(Item, Surface).Value!;
        Assert.Equal(second.OperationId, stored.OperationId);
        Assert.Equal(2, stored.Generation);
    }

    [Fact]
    public void SameGenerationAdvancesTheSameOperation()
    {
        var prepared = ArtworkOperationFixtures.Publication(generation: 1);
        _store.Write(prepared);

        var started = ArtworkOperationFixtures.Copy(
            prepared,
            phase: ArtworkOperationPhase.MutationStarted,
            updatedAt: ArtworkOperationFixtures.At.AddMinutes(1));
        _store.Write(started);

        var stored = _store.Read(Item, Surface).Value!;
        Assert.Equal(prepared.OperationId, stored.OperationId);
        Assert.Equal(ArtworkOperationPhase.MutationStarted, stored.Phase);
    }

    [Fact]
    public void SameGenerationDifferentOperationIsRejected()
    {
        _store.Write(ArtworkOperationFixtures.Publication(generation: 1));

        var conflicting = ArtworkOperationFixtures.Publication(generation: 1);

        Assert.Throws<InvalidOperationException>(() => _store.Write(conflicting));
    }

    [Fact]
    public void SameGenerationBackwardPhaseIsRejected()
    {
        var started = ArtworkOperationFixtures.Publication(
            generation: 1,
            phase: ArtworkOperationPhase.MutationStarted);
        _store.Write(started);

        var backward = ArtworkOperationFixtures.Copy(started, phase: ArtworkOperationPhase.Prepared);

        Assert.Throws<InvalidOperationException>(() => _store.Write(backward));
        Assert.Equal(ArtworkOperationPhase.MutationStarted, _store.Read(Item, Surface).Value!.Phase);
    }

    [Fact]
    public void SameGenerationCannotAdvancePastTerminalPhase()
    {
        var committed = ArtworkOperationFixtures.Publication(
            generation: 1,
            phase: ArtworkOperationPhase.Committed);
        _store.Write(committed);

        var aborted = ArtworkOperationFixtures.Copy(committed, phase: ArtworkOperationPhase.Aborted);

        Assert.Throws<InvalidOperationException>(() => _store.Write(aborted));
    }

    [Fact]
    public void InvalidOperationIsRejectedOnWrite()
    {
        var invalid = new ArtworkOperation(
            "op-0123456789abcdef",
            ArtworkOperationKind.Publication,
            Item,
            Surface,
            1,
            ArtworkStateFixtures.Present(Surface, "before-bytes"),
            ArtworkImagePresence.Present,
            ArtworkImagePresence.Present,
            ArtworkOperationPhase.Prepared,
            ArtworkLifecycleFence.Normal,
            ArtworkOperationFixtures.At,
            ArtworkOperationFixtures.At,
            ownershipToken: "own-0123456789abcdef",
            publicationToken: null,
            candidateAfterContentSha256: ArtworkHashes.ComputeSha256(new byte[] { 1, 2, 3 }),
            derivedArtifactId: "der-0123456789abcdef");

        Assert.Throws<ArgumentException>(() => _store.Write(invalid));
    }

    [Fact]
    public void CorruptEnvelopeIsQuarantinedAndNotReturned()
    {
        _store.Write(ArtworkOperationFixtures.Publication());
        var recordId = ArtworkOperationStore.GetRecordId(Item, Surface);
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ArtworkOperationStore.RecordKind,
            recordId);
        File.WriteAllText(path, "{ this is not valid json");

        var result = _store.Read(Item, Surface);

        Assert.Equal(StateReadStatus.InvalidQuarantined, result.Status);
        Assert.False(File.Exists(path));
        var quarantineDirectory = Path.Combine(_root, "quarantine", "authoritative", ArtworkOperationStore.RecordKind);
        Assert.True(Directory.Exists(quarantineDirectory));
        Assert.NotEmpty(Directory.GetFiles(quarantineDirectory, "*.json"));
    }

    [Fact]
    public void ValidEnvelopeWithInvalidSemanticsIsQuarantined()
    {
        var invalid = new ArtworkOperation(
            "op-0123456789abcdef",
            ArtworkOperationKind.Publication,
            Item,
            Surface,
            1,
            ArtworkStateFixtures.Present(Surface, "before-bytes"),
            ArtworkImagePresence.Present,
            ArtworkImagePresence.Present,
            ArtworkOperationPhase.Prepared,
            ArtworkLifecycleFence.Normal,
            ArtworkOperationFixtures.At,
            ArtworkOperationFixtures.At,
            ownershipToken: "own-0123456789abcdef",
            publicationToken: null,
            candidateAfterContentSha256: ArtworkHashes.ComputeSha256(new byte[] { 1, 2, 3 }),
            derivedArtifactId: "der-0123456789abcdef");

        var recordId = ArtworkOperationStore.GetRecordId(Item, Surface);
        var bytes = StateEnvelopeCodec.Serialize(
            StateAuthority.Authoritative,
            ArtworkOperationStore.RecordKind,
            recordId,
            invalid,
            DateTimeOffset.UtcNow,
            terminal: false);
        AtomicFileWriter.Write(
            _repository.Paths.GetRecordPath(StateAuthority.Authoritative, ArtworkOperationStore.RecordKind, recordId),
            bytes);

        var result = _store.Read(Item, Surface);

        Assert.Equal(StateReadStatus.InvalidQuarantined, result.Status);
    }

    [Fact]
    public void NonTerminalOperationIsNeverPrunedByRetention()
    {
        var repository = new StateRepository(_root, new OperationalLimits { TerminalProvenanceRetentionDays = 1 });
        var store = new ArtworkOperationStore(repository);
        var operation = ArtworkOperationFixtures.Publication(phase: ArtworkOperationPhase.Prepared);
        WriteAgedEnvelope(repository, operation, DateTimeOffset.UtcNow.AddDays(-30), terminal: false);

        repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.Equal(StateReadStatus.Found, store.Read(Item, Surface).Status);
    }

    [Fact]
    public void TerminalOperationIsPrunedByRetention()
    {
        var repository = new StateRepository(_root, new OperationalLimits { TerminalProvenanceRetentionDays = 1 });
        var store = new ArtworkOperationStore(repository);
        var operation = ArtworkOperationFixtures.Publication(phase: ArtworkOperationPhase.Committed);
        WriteAgedEnvelope(repository, operation, DateTimeOffset.UtcNow.AddDays(-30), terminal: true);

        repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.Equal(StateReadStatus.Missing, store.Read(Item, Surface).Status);
    }

    [Fact]
    public void TombstonedItemRemovalIsTerminalAndRetainedThenPruned()
    {
        var repository = new StateRepository(_root, new OperationalLimits { TerminalProvenanceRetentionDays = 1 });
        var store = new ArtworkOperationStore(repository);
        var tombstone = ArtworkOperationFixtures.Tombstone();
        Assert.True(tombstone.IsTerminal);
        Assert.Equal(ArtworkLifecycleFence.ItemRemoved, tombstone.LifecycleFence);

        WriteAgedEnvelope(repository, tombstone, DateTimeOffset.UtcNow.AddDays(-30), terminal: true);

        repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.Equal(StateReadStatus.Missing, store.Read(Item, Surface).Status);
    }

    [Fact]
    public void RecordIdIsStableAndPathSafe()
    {
        var recordId = ArtworkOperationStore.GetRecordId(Item, Surface);

        Assert.Equal("aaaaaaaabbbbccccddddeeeeeeeeeeee-Primary", recordId);
        Assert.DoesNotContain("/", recordId, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", recordId, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadForAnotherSubjectReturnsMissing()
    {
        _store.Write(ArtworkOperationFixtures.Publication());

        var result = _store.Read(Guid.NewGuid(), Surface);

        Assert.Equal(StateReadStatus.Missing, result.Status);
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

    private static void WriteAgedEnvelope(
        StateRepository repository,
        ArtworkOperation operation,
        DateTimeOffset updatedAt,
        bool terminal)
    {
        var recordId = ArtworkOperationStore.GetRecordId(operation.JellyfinItemId, operation.ImageSurface);
        var bytes = StateEnvelopeCodec.Serialize(
            StateAuthority.Authoritative,
            ArtworkOperationStore.RecordKind,
            recordId,
            operation,
            updatedAt,
            terminal);
        AtomicFileWriter.Write(
            repository.Paths.GetRecordPath(StateAuthority.Authoritative, ArtworkOperationStore.RecordKind, recordId),
            bytes);
    }
}
