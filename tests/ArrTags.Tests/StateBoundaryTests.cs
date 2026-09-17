using System;
using System.IO;
using System.Text.Json;
using ArrTags.Configuration;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Foundation-level checks for the versioned plugin state boundary: atomic
/// writes, integrity and schema handling, cache discard versus authoritative
/// quarantine, path safety, and bounded retention. These tests require no live
/// Jellyfin or Arr instance.
/// </summary>
public sealed class StateBoundaryTests : IDisposable
{
    private readonly string _root;
    private readonly StateRepository _repository;

    public StateBoundaryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
    }

    [Fact]
    public void RoundTripsCacheRecord()
    {
        _repository.Write(StateAuthority.Cache, "metadata", "item-1", new TestPayload { Name = "a", Count = 2 });

        var result = _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "item-1");

        Assert.Equal(StateReadStatus.Found, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("a", result.Value!.Name);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public void AtomicWriteReplacesExistingRecord()
    {
        _repository.Write(StateAuthority.Cache, "metadata", "item-1", new TestPayload { Name = "first" });
        _repository.Write(StateAuthority.Cache, "metadata", "item-1", new TestPayload { Name = "second" });

        var result = _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "item-1");

        Assert.Equal(StateReadStatus.Found, result.Status);
        Assert.Equal("second", result.Value!.Name);
    }

    [Fact]
    public void MissingRecordIsReportedAsMissing()
    {
        var result = _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "absent");

        Assert.Equal(StateReadStatus.Missing, result.Status);
    }

    [Fact]
    public void CorruptCacheRecordIsDiscardedAndCanBeRebuilt()
    {
        _repository.Write(StateAuthority.Cache, "metadata", "item-1", new TestPayload { Name = "x" });
        var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, "metadata", "item-1");
        File.WriteAllText(path, "{ this is not valid json");

        var result = _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "item-1");

        Assert.Equal(StateReadStatus.InvalidDiscarded, result.Status);
        Assert.False(File.Exists(path));

        _repository.Write(StateAuthority.Cache, "metadata", "item-1", new TestPayload { Name = "rebuilt" });
        Assert.Equal(StateReadStatus.Found, _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "item-1").Status);
    }

    [Fact]
    public void IntegrityMismatchIsQuarantinedForAuthoritativeState()
    {
        _repository.Write(StateAuthority.Authoritative, "artwork", "item-1", new TestPayload { Name = "x" });
        var path = _repository.Paths.GetRecordPath(StateAuthority.Authoritative, "artwork", "item-1");

        var envelope = JsonSerializer.Deserialize<StateEnvelope>(File.ReadAllBytes(path))!;
        envelope.PayloadJson = "{\"Name\":\"tampered\",\"Count\":0}";
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(envelope));

        var result = _repository.Read<TestPayload>(StateAuthority.Authoritative, "artwork", "item-1");

        Assert.Equal(StateReadStatus.InvalidQuarantined, result.Status);
        Assert.False(File.Exists(path));

        var quarantineDirectory = Path.Combine(_root, "quarantine", "authoritative", "artwork");
        Assert.True(Directory.Exists(quarantineDirectory));
        Assert.NotEmpty(Directory.GetFiles(quarantineDirectory, "*.json"));
    }

    [Fact]
    public void IncompatibleSchemaVersionIsDiscardedForCache()
    {
        var bytes = StateEnvelopeCodec.Serialize(
            StateAuthority.Cache,
            "metadata",
            "item-1",
            new TestPayload { Name = "x" },
            DateTimeOffset.UtcNow,
            terminal: false);
        var envelope = JsonSerializer.Deserialize<StateEnvelope>(bytes)!;
        envelope.SchemaVersion = 999;

        var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, "metadata", "item-1");
        AtomicFileWriter.Write(path, JsonSerializer.SerializeToUtf8Bytes(envelope));

        var result = _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "item-1");

        Assert.Equal(StateReadStatus.InvalidDiscarded, result.Status);
    }

    [Fact]
    public void PathTraversalIdentifiersAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "../escape"));
        Assert.Throws<ArgumentException>(() =>
            _repository.Read<TestPayload>(StateAuthority.Cache, "nested/kind", "item"));
        Assert.Throws<ArgumentException>(() =>
            _repository.Write(StateAuthority.Cache, "../evil", "item", new TestPayload()));
    }

    [Fact]
    public void UnreadableCacheRecordIsDiscarded()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        _repository.Write(StateAuthority.Cache, "metadata", "item-1", new TestPayload { Name = "x" });
        var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, "metadata", "item-1");
        File.SetUnixFileMode(path, UnixFileMode.None);

        var result = _repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "item-1");

        Assert.Equal(StateReadStatus.InvalidDiscarded, result.Status);
    }

    [Fact]
    public void RestartRecoveryKeepsHealthyRecordsWhenAnotherIsCorrupt()
    {
        _repository.Write(StateAuthority.Cache, "metadata", "good", new TestPayload { Name = "good" });
        _repository.Write(StateAuthority.Cache, "metadata", "bad", new TestPayload { Name = "bad" });
        File.WriteAllText(_repository.Paths.GetRecordPath(StateAuthority.Cache, "metadata", "bad"), "broken");

        var restarted = new StateRepository(_root);

        Assert.Equal(StateReadStatus.Found, restarted.Read<TestPayload>(StateAuthority.Cache, "metadata", "good").Status);
        Assert.Equal(StateReadStatus.InvalidDiscarded, restarted.Read<TestPayload>(StateAuthority.Cache, "metadata", "bad").Status);
    }

    [Fact]
    public void CacheRetentionPrunesExpiredCacheButNeverAuthoritative()
    {
        var limits = new OperationalLimits { RenderCacheTtlMinutes = 1 };
        var repository = new StateRepository(_root, limits);
        repository.Write(StateAuthority.Authoritative, "artwork", "item-1", new TestPayload { Name = "auth" });
        repository.Write(StateAuthority.Cache, "metadata", "item-1", new TestPayload { Name = "cache" });

        var cachePath = repository.Paths.GetRecordPath(StateAuthority.Cache, "metadata", "item-1");
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddHours(-2));

        var removed = repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.True(removed >= 1);
        Assert.Equal(StateReadStatus.Missing, repository.Read<TestPayload>(StateAuthority.Cache, "metadata", "item-1").Status);
        Assert.Equal(StateReadStatus.Found, repository.Read<TestPayload>(StateAuthority.Authoritative, "artwork", "item-1").Status);
    }

    [Fact]
    public void AuthoritativeRetentionPrunesOnlyTerminalRecords()
    {
        var limits = new OperationalLimits { TerminalProvenanceRetentionDays = 1 };
        var repository = new StateRepository(_root, limits);
        var old = DateTimeOffset.UtcNow.AddDays(-5);

        AtomicFileWriter.Write(
            repository.Paths.GetRecordPath(StateAuthority.Authoritative, "artwork", "terminal"),
            StateEnvelopeCodec.Serialize(StateAuthority.Authoritative, "artwork", "terminal", new TestPayload { Name = "t" }, old, terminal: true));
        AtomicFileWriter.Write(
            repository.Paths.GetRecordPath(StateAuthority.Authoritative, "artwork", "active"),
            StateEnvelopeCodec.Serialize(StateAuthority.Authoritative, "artwork", "active", new TestPayload { Name = "a" }, old, terminal: false));

        var removed = repository.ApplyRetention(DateTimeOffset.UtcNow);

        Assert.Equal(1, removed);
        Assert.Equal(StateReadStatus.Missing, repository.Read<TestPayload>(StateAuthority.Authoritative, "artwork", "terminal").Status);
        Assert.Equal(StateReadStatus.Found, repository.Read<TestPayload>(StateAuthority.Authoritative, "artwork", "active").Status);
    }

    [Fact]
    public void OversizedRecordIsRejected()
    {
        var payload = new TestPayload { Name = new string('x', StateRepository.MaxStateRecordBytes) };

        Assert.Throws<InvalidOperationException>(() =>
            _repository.Write(StateAuthority.Cache, "metadata", "big", payload));
    }

    public void Dispose()
    {
        TryDeleteDirectory(_root);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A minimal serializable payload used by the state boundary tests.
    /// </summary>
    public sealed class TestPayload
    {
        /// <summary>
        /// Gets or sets a name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a count.
        /// </summary>
        public int Count { get; set; }
    }
}
