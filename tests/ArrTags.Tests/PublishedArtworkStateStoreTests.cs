using System;
using System.IO;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks that <see cref="PublishedArtworkState"/> persists through the
/// authoritative versioned state boundary: round-trip, corruption quarantine,
/// semantic-invariant rejection, and terminal-only retention. No live Jellyfin
/// or Arr instance is required and no image mutation is performed.
/// </summary>
public sealed class PublishedArtworkStateStoreTests : IDisposable
{
    private static readonly Guid Item = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(5);

    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly PublishedArtworkStateStore _store;

    public PublishedArtworkStateStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-state-artwork-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new PublishedArtworkStateStore(_repository);
    }

    [Fact]
    public void WriteAndReadRoundTripsAPublishedStateThroughTheAuthoritativeBoundary()
    {
        var state = ArtworkStateFixtures.Published(Item, Surface);

        _store.Write(state);
        var result = _store.Read(Item, Surface);

        Assert.Equal(StateReadStatus.Found, result.Status);
        var stored = result.Value!;
        Assert.Equal(Item, stored.JellyfinItemId);
        Assert.Equal(ArtworkPublicationState.Published, stored.State);
        Assert.Equal(state.OwnershipToken, stored.OwnershipToken);
        Assert.Equal(state.PublicationToken, stored.PublicationToken);
        Assert.Equal(state.ActiveImageIdentity!.ContentSha256, stored.ActiveImageIdentity!.ContentSha256);
        Assert.Equal(state.ActiveImageIdentity.Width, stored.ActiveImageIdentity.Width);
        Assert.Equal(state.ActiveImageIdentity.JellyfinImageTag, stored.ActiveImageIdentity.JellyfinImageTag);
        Assert.Equal(state.LastOwnershipObservation!.Status, stored.LastOwnershipObservation!.Status);
        Assert.True(stored.Validate(out var reason), reason);
    }

    [Fact]
    public void CorruptEnvelopeIsQuarantinedAndNotReturnedAsState()
    {
        _store.Write(ArtworkStateFixtures.Published(Item, Surface));
        var recordId = PublishedArtworkStateStore.GetRecordId(Item, Surface);
        var path = _repository.Paths.GetRecordPath(StateAuthority.Authoritative, PublishedArtworkStateStore.RecordKind, recordId);
        File.WriteAllText(path, "{ this is not valid json");

        var result = _store.Read(Item, Surface);

        Assert.Equal(StateReadStatus.InvalidQuarantined, result.Status);
        Assert.False(File.Exists(path));
        var quarantineDirectory = Path.Combine(_root, "quarantine", "authoritative", PublishedArtworkStateStore.RecordKind);
        Assert.True(Directory.Exists(quarantineDirectory));
        Assert.NotEmpty(Directory.GetFiles(quarantineDirectory, "*.json"));
    }

    [Fact]
    public void ValidEnvelopeWithInvalidSemanticsIsQuarantined()
    {
        var invalid = new PublishedArtworkState(Item, Surface, ArtworkPublicationState.Published, At);
        var recordId = PublishedArtworkStateStore.GetRecordId(Item, Surface);
        var bytes = StateEnvelopeCodec.Serialize(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            recordId,
            invalid,
            DateTimeOffset.UtcNow,
            terminal: false);
        AtomicFileWriter.Write(
            _repository.Paths.GetRecordPath(StateAuthority.Authoritative, PublishedArtworkStateStore.RecordKind, recordId),
            bytes);

        var result = _store.Read(Item, Surface);

        Assert.Equal(StateReadStatus.InvalidQuarantined, result.Status);
    }

    [Fact]
    public void WriteRejectsAStateThatViolatesTheDocumentedInvariants()
    {
        var invalid = new PublishedArtworkState(Item, Surface, ArtworkPublicationState.Published, At);

        Assert.Throws<ArgumentException>(() => _store.Write(invalid));
    }

    [Fact]
    public void NonTerminalPublishedStateIsNeverPrunedByRetention()
    {
        var repository = new StateRepository(_root, new OperationalLimits { TerminalProvenanceRetentionDays = 1 });
        var store = new PublishedArtworkStateStore(repository);
        var state = ArtworkStateFixtures.Published(Item, Surface);
        WriteAgedEnvelope(repository, state, DateTimeOffset.UtcNow.AddDays(-30), terminal: false);

        repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.Equal(StateReadStatus.Found, store.Read(Item, Surface).Status);
    }

    [Fact]
    public void TerminalStateIsPrunedByRetention()
    {
        var repository = new StateRepository(_root, new OperationalLimits { TerminalProvenanceRetentionDays = 1 });
        var store = new PublishedArtworkStateStore(repository);
        var published = ArtworkStateFixtures.Published(Item, Surface);
        var lost = PublishedArtworkStateTransitions
            .BeginPublication(published, ArtworkStateFixtures.Present(Surface, "external"), At)
            .State;
        Assert.Equal(ArtworkPublicationState.OwnershipLost, lost.State);
        WriteAgedEnvelope(repository, lost, DateTimeOffset.UtcNow.AddDays(-30), terminal: true);

        repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.Equal(StateReadStatus.Missing, store.Read(Item, Surface).Status);
    }

    [Fact]
    public void RecordIdIsStableAndPathSafe()
    {
        var recordId = PublishedArtworkStateStore.GetRecordId(Item, Surface);

        Assert.Equal("aaaaaaaabbbbccccddddeeeeeeeeeeee-Primary", recordId);
        Assert.DoesNotContain("/", recordId, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", recordId, StringComparison.Ordinal);
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
        PublishedArtworkState state,
        DateTimeOffset updatedAt,
        bool terminal)
    {
        var recordId = PublishedArtworkStateStore.GetRecordId(state.JellyfinItemId, state.ImageSurface);
        var bytes = StateEnvelopeCodec.Serialize(
            StateAuthority.Authoritative,
            PublishedArtworkStateStore.RecordKind,
            recordId,
            state,
            updatedAt,
            terminal);
        AtomicFileWriter.Write(
            repository.Paths.GetRecordPath(StateAuthority.Authoritative, PublishedArtworkStateStore.RecordKind, recordId),
            bytes);
    }
}
