using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArrTags.Configuration;
using ArrTags.Reconciliation;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the task 19.1 persisted reconciliation cursor record and
/// its cache boundary (ADR-022 clauses 3 through 5 as amended by ADR-029): the
/// fixed kind and identifier under the cache authority, the bounded
/// secret-free payload, the round-trip, the scope-reset rule, and the
/// missing/torn/out-of-range reset to the start. No live Jellyfin or Arr
/// instance is required.
/// </summary>
public sealed class ReconciliationCursorStoreTests : IDisposable
{
    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly ReconciliationCursorStore _store;

    public ReconciliationCursorStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-cursor-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new ReconciliationCursorStore(_repository);
    }

    [Fact]
    public void CursorRecordUsesTheFixedCacheKindAndIdentifier()
    {
        Assert.Equal("reconciliation-cursor", ReconciliationCursorStore.RecordKind);
        Assert.Equal("current", ReconciliationCursorStore.RecordId);

        var scope = Scope();
        _store.Write(scope, new ReconciliationCursorPosition("example movie", Guid.NewGuid()));

        var cachePath = _repository.Paths.GetRecordPath(
            StateAuthority.Cache,
            ReconciliationCursorStore.RecordKind,
            ReconciliationCursorStore.RecordId);
        var authoritativePath = _repository.Paths.GetRecordPath(
            StateAuthority.Authoritative,
            ReconciliationCursorStore.RecordKind,
            ReconciliationCursorStore.RecordId);

        Assert.True(File.Exists(cachePath), "The cursor record must be written under the cache authority.");
        Assert.Contains("cache", Path.GetDirectoryName(cachePath)!, StringComparison.Ordinal);
        Assert.False(File.Exists(authoritativePath), "The cursor record must never be written as authoritative state.");
    }

    [Fact]
    public void ScopeAndPositionRoundTripThroughTheCacheBoundary()
    {
        var scope = Scope("library-b", "library-a");
        var itemId = Guid.NewGuid();

        _store.Write(scope, new ReconciliationCursorPosition("example movie", itemId));

        var result = _store.Read(Scope("library-b", "library-a"));
        Assert.Equal(ReconciliationCursorStatus.Position, result.Status);
        Assert.True(result.HasPosition);
        Assert.Equal(itemId, result.Position!.Value.ItemId);
        Assert.Equal("example movie", result.Position!.Value.SortName);
    }

    [Fact]
    public void WrappedRecordRoundTripsWithoutAPosition()
    {
        _store.Write(Scope(), position: null);

        var result = _store.Read(Scope());

        Assert.Equal(ReconciliationCursorStatus.Wrapped, result.Status);
        Assert.False(result.HasPosition);
    }

    [Fact]
    public void EquivalentButDifferentlyOrderedScopeIsNotAScopeChange()
    {
        // The sorted set is the recorded identity: a record written with a
        // differently ordered and duplicated identifier list is still the same
        // scope (the set is normalized on read).
        var record = new ReconciliationCursor(
            ReconciliationCursor.CurrentCursorVersion,
            new[] { "library-b", " library-a ", "library-b" },
            moviePosters: true,
            episodePosters: false,
            sortName: "example movie",
            itemId: Guid.NewGuid());
        WriteRaw(record);

        var result = _store.Read(Scope("library-a", "library-b"));

        Assert.Equal(ReconciliationCursorStatus.Position, result.Status);
    }

    [Theory]
    [InlineData("library-added")]
    [InlineData("library-removed")]
    [InlineData("movie-scope")]
    [InlineData("episode-scope")]
    public void ScopeChangeResetsToTheStart(string change)
    {
        var current = Scope("library-a");
        var recorded = change switch
        {
            "library-added" => Scope("library-a", "library-b"),
            "library-removed" => Scope(),
            "movie-scope" => ScopeWith(moviePosters: false, episodePosters: false),
            "episode-scope" => ScopeWith(moviePosters: true, episodePosters: true),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        _store.Write(recorded, new ReconciliationCursorPosition("example movie", Guid.NewGuid()));

        var result = _store.Read(current);

        Assert.Equal(ReconciliationCursorStatus.ScopeChanged, result.Status);
        Assert.False(result.HasPosition);
    }

    [Fact]
    public void MissingRecordIsReportedAsMissingAndResetsToTheStart()
    {
        var result = _store.Read(Scope());

        Assert.Equal(ReconciliationCursorStatus.Missing, result.Status);
        Assert.False(result.HasPosition);
    }

    [Fact]
    public void TornRecordIsDiscardedAsACacheMissAndResetsToTheStart()
    {
        _store.Write(Scope(), new ReconciliationCursorPosition("example movie", Guid.NewGuid()));
        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Cache,
            ReconciliationCursorStore.RecordKind,
            ReconciliationCursorStore.RecordId);
        File.WriteAllText(path, "{\"SchemaVersion\":1,\"PayloadJson\":\"torn");

        var result = _store.Read(Scope());

        Assert.Equal(ReconciliationCursorStatus.Discarded, result.Status);
        Assert.False(result.HasPosition);

        // Cache authority: the invalid record is discarded (observed as a miss)
        // rather than quarantined or treated as an authoritative failure.
        Assert.False(File.Exists(path));
        Assert.Equal(
            ReconciliationCursorStatus.Missing,
            _store.Read(Scope()).Status);
    }

    [Fact]
    public void TamperedIntegrityHashIsDiscarded()
    {
        var record = new ReconciliationCursor(
            ReconciliationCursor.CurrentCursorVersion,
            Array.Empty<string>(),
            moviePosters: true,
            episodePosters: false,
            sortName: "example movie",
            itemId: Guid.NewGuid());
        _repository.Write(StateAuthority.Cache, ReconciliationCursorStore.RecordKind, ReconciliationCursorStore.RecordId, record);

        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Cache,
            ReconciliationCursorStore.RecordKind,
            ReconciliationCursorStore.RecordId);
        var text = File.ReadAllText(path);
        File.WriteAllText(path, text.Replace("\"PayloadSha256\":\"", "\"PayloadSha256\":\"00", StringComparison.Ordinal));

        Assert.Equal(ReconciliationCursorStatus.Discarded, _store.Read(Scope()).Status);
    }

    [Fact]
    public void IncompatibleCursorVersionIsDiscarded()
    {
        WriteRaw(new ReconciliationCursor(
            cursorVersion: 99,
            Array.Empty<string>(),
            moviePosters: true,
            episodePosters: false,
            sortName: "example movie",
            itemId: Guid.NewGuid()));

        var result = _store.Read(Scope());

        Assert.Equal(ReconciliationCursorStatus.Discarded, result.Status);
        Assert.False(result.HasPosition);
    }

    [Theory]
    [InlineData("too-many-libraries")]
    [InlineData("empty-library-id")]
    [InlineData("oversized-library-id")]
    [InlineData("empty-item-id")]
    [InlineData("position-without-sort-name")]
    [InlineData("sort-name-without-position")]
    [InlineData("oversized-sort-name")]
    public void OutOfBoundsPayloadIsDiscardedAsARebuildableMiss(string violation)
    {
        var libraryIds = new List<string> { "library-a" };
        Guid? itemId = Guid.NewGuid();
        string? sortName = "example movie";

        switch (violation)
        {
            case "too-many-libraries":
                libraryIds.Clear();
                for (var index = 0; index <= ReconciliationCursor.MaxEnabledLibraryIds; index++)
                {
                    libraryIds.Add(FormattableString.Invariant($"library-{index}"));
                }

                break;
            case "empty-library-id":
                libraryIds.Add(" ");
                break;
            case "oversized-library-id":
                libraryIds.Add(new string('a', ReconciliationCursor.MaxEnabledLibraryIdLength + 1));
                break;
            case "empty-item-id":
                itemId = Guid.Empty;
                break;
            case "position-without-sort-name":
                sortName = null;
                break;
            case "sort-name-without-position":
                itemId = null;
                break;
            case "oversized-sort-name":
                sortName = new string('a', ReconciliationCursor.MaxSortNameHintLength + 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(violation));
        }

        WriteRaw(new ReconciliationCursor(
            ReconciliationCursor.CurrentCursorVersion,
            libraryIds,
            moviePosters: true,
            episodePosters: false,
            sortName,
            itemId));

        var result = _store.Read(Scope());

        Assert.Equal(ReconciliationCursorStatus.Discarded, result.Status);
        Assert.False(result.HasPosition);
    }

    [Fact]
    public void SortNameHintIsTruncatedToTheBoundAndTheItemIdRemainsTheAnchor()
    {
        var itemId = Guid.NewGuid();
        var hint = new string('a', ReconciliationCursor.MaxSortNameHintLength + 100);

        _store.Write(Scope(), new ReconciliationCursorPosition(hint, itemId));

        var result = _store.Read(Scope());
        Assert.Equal(ReconciliationCursorStatus.Position, result.Status);
        Assert.Equal(ReconciliationCursor.MaxSortNameHintLength, result.Position!.Value.SortName.Length);
        Assert.Equal(itemId, result.Position!.Value.ItemId);
    }

    [Fact]
    public void AnEmptySortNameHintIsAValidPosition()
    {
        var itemId = Guid.NewGuid();
        _store.Write(Scope(), new ReconciliationCursorPosition(null, itemId));

        var result = _store.Read(Scope());

        Assert.Equal(ReconciliationCursorStatus.Position, result.Status);
        Assert.Equal(string.Empty, result.Position!.Value.SortName);
        Assert.Equal(itemId, result.Position!.Value.ItemId);
    }

    [Fact]
    public void APositionRequiresANonEmptyItemId()
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationCursorPosition("example", Guid.Empty));
    }

    [Fact]
    public void WriteRejectsAnOutOfBoundsScope()
    {
        // The write validates the record before persisting it: an oversized
        // enabled-library identifier cannot produce a persisted record.
        var configuration = new PluginConfiguration();
        configuration.EnabledLibraries.Add(new string('a', ReconciliationCursor.MaxEnabledLibraryIdLength + 1));
        var scope = ReconciliationScopeIdentity.From(PluginConfigurationSnapshot.From(configuration));

        Assert.Throws<ArgumentException>(
            () => _store.Write(scope, new ReconciliationCursorPosition("example", Guid.NewGuid())));
    }

    [Fact]
    public void PersistedRecordCarriesOnlyTheScopeIdentityAndThePosition()
    {
        // Structural: the payload property set is fixed and carries no path,
        // provider payload, or credential field.
        var properties = typeof(ReconciliationCursor)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "CursorVersion", "EnabledLibraryIds", "EpisodePosters", "ItemId", "MoviePosters", "SortName" },
            properties);

        _store.Write(Scope("library-a"), new ReconciliationCursorPosition("example movie", Guid.NewGuid()));

        var path = _repository.Paths.GetRecordPath(
            StateAuthority.Cache,
            ReconciliationCursorStore.RecordKind,
            ReconciliationCursorStore.RecordId);
        var text = File.ReadAllText(path);

        Assert.Contains("library-a", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/media/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("api-key", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
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

    private void WriteRaw(ReconciliationCursor record)
    {
        _repository.Write(StateAuthority.Cache, ReconciliationCursorStore.RecordKind, ReconciliationCursorStore.RecordId, record);
    }

    private static ReconciliationScopeIdentity Scope(params string[] libraryIds)
    {
        return ScopeWith(moviePosters: true, episodePosters: false, libraryIds);
    }

    private static ReconciliationScopeIdentity ScopeWith(
        bool moviePosters,
        bool episodePosters,
        params string[] libraryIds)
    {
        var configuration = new PluginConfiguration
        {
            BadgeMoviePosters = moviePosters,
            BadgeEpisodePosters = episodePosters,
        };

        foreach (var libraryId in libraryIds)
        {
            configuration.EnabledLibraries.Add(libraryId);
        }

        return ReconciliationScopeIdentity.From(PluginConfigurationSnapshot.From(configuration));
    }
}
