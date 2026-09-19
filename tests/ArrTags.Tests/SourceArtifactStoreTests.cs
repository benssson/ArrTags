using System;
using System.IO;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.State;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Focused checks for the content-addressed retained source-artifact store:
/// round-trip and integrity, bounded size/format/hash validation, traversal
/// safety, atomic promotion, and authoritative (non-cache) quota behavior.
/// </summary>
public sealed class SourceArtifactStoreTests : IDisposable
{
    private readonly string _root;
    private readonly StateRepository _repository;
    private readonly SourceArtifactStore _store;

    public SourceArtifactStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "arrtags-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _repository = new StateRepository(_root);
        _store = new SourceArtifactStore(_repository);
    }

    [Fact]
    public void PromoteAndReadRoundTripsBytesAndMetadata()
    {
        var bytes = PngBytes(64);

        var promoted = _store.Promote(bytes, "image/png");

        Assert.True(promoted.Succeeded);
        var info = promoted.Info!;
        Assert.Equal(bytes.Length, info.ByteLength);
        Assert.Equal(ArtworkHashes.ComputeSha256(bytes), info.ArtifactId);

        var read = _store.Read(info.ArtifactId);

        Assert.Equal(SourceArtifactReadStatus.Found, read.Status);
        Assert.Equal(bytes, read.Bytes.ToArray());
        Assert.Equal("image/png", read.Info!.ContentType);
    }

    [Fact]
    public void PromoteNormalizesTheJpgAlias()
    {
        var bytes = JpegBytes(32);

        var promoted = _store.Promote(bytes, "image/jpg");

        Assert.True(promoted.Succeeded);
        Assert.Equal("image/jpeg", promoted.Info!.ContentType);
    }

    [Fact]
    public void PromoteIsIdempotentForIdenticalBytes()
    {
        var bytes = PngBytes(32);

        var first = _store.Promote(bytes, "image/png");
        var second = _store.Promote(bytes, "image/png");

        Assert.True(second.Succeeded);
        Assert.Equal(first.Info!.ArtifactId, second.Info!.ArtifactId);
        Assert.Equal(bytes.Length, _store.GetTotalArtifactBytes());
    }

    [Fact]
    public void PromoteRejectsEmptySource()
    {
        var result = _store.Promote(ReadOnlySpan<byte>.Empty, "image/png");

        Assert.False(result.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.InvalidContent, result.Failure);
    }

    [Fact]
    public void PromoteRejectsOversizedSource()
    {
        var store = new SourceArtifactStore(new StateRepository(_root, new OperationalLimits { SourceArtifactLimitBytes = 4 }));

        var result = store.Promote(PngBytes(64), "image/png");

        Assert.False(result.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.TooLarge, result.Failure);
    }

    [Fact]
    public void PromoteRejectsUnsupportedContentType()
    {
        var result = _store.Promote(PngBytes(16), "text/plain");

        Assert.False(result.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.UnsupportedFormat, result.Failure);
    }

    [Fact]
    public void PromoteRejectsDeclaredHashMismatch()
    {
        var result = _store.Promote(PngBytes(16), "image/png", new string('0', 64));

        Assert.False(result.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.HashMismatch, result.Failure);
    }

    [Fact]
    public void PromoteRejectsFormatMismatch()
    {
        var result = _store.Promote(JpegBytes(16), "image/png");

        Assert.False(result.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.UnsupportedFormat, result.Failure);
    }

    [Fact]
    public void PromoteAcceptsAnUnrecognizedSignatureWithADeclaredImageType()
    {
        var bytes = new byte[16];

        var result = _store.Promote(bytes, "image/png");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ReadReturnsMissingForAnUnknownArtifact()
    {
        var result = _store.Read(new string('A', 64));

        Assert.Equal(SourceArtifactReadStatus.Missing, result.Status);
    }

    [Fact]
    public void ReadReturnsInvalidForATraversalIdentifier()
    {
        var result = _store.Read("../escape");

        Assert.Equal(SourceArtifactReadStatus.Invalid, result.Status);
    }

    [Fact]
    public void GetArtifactPathRejectsATraversalIdentifier()
    {
        Assert.Throws<ArgumentException>(() => _store.Paths.GetArtifactPath("../escape"));
    }

    [Fact]
    public void ReadReturnsCorruptWhenTheBytesAreTampered()
    {
        var bytes = PngBytes(32);
        var promoted = _store.Promote(bytes, "image/png").Info!;
        File.WriteAllBytes(_store.Paths.GetArtifactPath(promoted.ArtifactId), PngBytes(48));

        var result = _store.Read(promoted.ArtifactId);

        Assert.Equal(SourceArtifactReadStatus.Corrupt, result.Status);
    }

    [Fact]
    public void ReadReturnsMissingWhenTheManifestIsAbsent()
    {
        var bytes = PngBytes(32);
        var promoted = _store.Promote(bytes, "image/png").Info!;
        File.Delete(_repository.Paths.GetRecordPath(StateAuthority.Authoritative, SourceArtifactStore.ManifestKind, promoted.ArtifactId));

        var result = _store.Read(promoted.ArtifactId);

        Assert.Equal(SourceArtifactReadStatus.Missing, result.Status);
    }

    [Fact]
    public void CorruptManifestIsQuarantinedAndReadAsCorrupt()
    {
        var promoted = _store.Promote(PngBytes(32), "image/png").Info!;
        var path = _repository.Paths.GetRecordPath(StateAuthority.Authoritative, SourceArtifactStore.ManifestKind, promoted.ArtifactId);
        File.WriteAllText(path, "{ broken");

        var result = _store.Read(promoted.ArtifactId);

        Assert.Equal(SourceArtifactReadStatus.Corrupt, result.Status);
        Assert.False(File.Exists(path));
        Assert.True(Directory.Exists(Path.Combine(_root, "quarantine", "authoritative", SourceArtifactStore.ManifestKind)));
    }

    [Fact]
    public void PromoteRejectsWhenTheAuthoritativeQuotaWouldBeExceeded()
    {
        var store = new SourceArtifactStore(new StateRepository(_root, new OperationalLimits { ArtifactStorageQuotaBytes = 4 }));

        var result = store.Promote(PngBytes(32), "image/png");

        Assert.False(result.Succeeded);
        Assert.Equal(SourceArtifactPromotionFailure.StorageQuotaExceeded, result.Failure);
    }

    [Fact]
    public void AuthoritativeManifestSurvivesCacheRetention()
    {
        var bytes = PngBytes(32);
        var promoted = _store.Promote(bytes, "image/png").Info!;

        _repository.ApplyRetention(DateTimeOffset.UtcNow.AddYears(10));

        Assert.Equal(SourceArtifactReadStatus.Found, _store.Read(promoted.ArtifactId).Status);
    }

    [Fact]
    public void AtomicPromotionLeavesNoTemporaryFiles()
    {
        _store.Promote(PngBytes(32), "image/png");

        var temporaryFiles = Directory.GetFiles(_root, "*.tmp-*", SearchOption.AllDirectories);

        Assert.Empty(temporaryFiles);
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

    private static byte[] PngBytes(int length)
    {
        var bytes = new byte[Math.Max(length, 8)];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        for (var index = signature.Length; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        return bytes;
    }

    private static byte[] JpegBytes(int length)
    {
        var bytes = new byte[Math.Max(length, 3)];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        for (var index = 3; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        return bytes;
    }
}
