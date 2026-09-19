using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.3 focused checks for the host-neutral source adapter core. An
/// in-memory <see cref="IArtworkImageAccess"/> fake supplies the observation, so
/// these tests run without a live Jellyfin host or a native raster runtime and
/// cover presence, absence, failure, content-type confinement, the configured
/// byte and dimension limits, oriented display dimensions, and hash/length
/// correctness. They also pin that the boundary exposes no path or Jellyfin
/// entity.
/// </summary>
public sealed class ArtworkSourceReaderTests
{
    private static readonly Guid Item = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;
    private static readonly ArtworkImageSurface IndexedSurface = new(ArtworkImageType.Primary, 1);
    private static readonly DateTimeOffset Modified = DateTimeOffset.UnixEpoch.AddDays(3);

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    // ---- Present ---------------------------------------------------------------

    [Fact]
    public async Task PresentPngProducesAConfinedResultAndAValidRendererInput()
    {
        var reader = Reader(() => ArtworkImageAccessResult.Present(PngBytes, 500, 750, SourceOrientation.TopLeft, Modified, "tag-1"));

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Present, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.None, result.FailureReason);
        Assert.Equal(Surface, result.Surface);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(PngBytes.LongLength, result.ByteLength);
        Assert.Equal(ArtworkHashes.ComputeSha256(PngBytes), result.ContentSha256);
        Assert.Equal(500, result.OrientedWidth);
        Assert.Equal(750, result.OrientedHeight);
        Assert.Equal(Modified, result.DateModifiedUtc);
        Assert.Equal("tag-1", result.JellyfinImageTag);

        Assert.True(result.TryCreateSourceImageInput(out var input));
        Assert.NotNull(input);
        Assert.Equal("image/png", input!.ContentType);
        Assert.Equal(500, input.OrientedWidth);
        Assert.Equal(750, input.OrientedHeight);
        Assert.Equal(ArtworkHashes.ComputeSha256(PngBytes), input.SourceSha256);
        Assert.True(input.Bytes.Span.SequenceEqual(PngBytes));

        Assert.True(result.TryCreateActiveImageIdentity(out var identity));
        Assert.NotNull(identity);
        Assert.Equal(ArtworkImagePresence.Present, identity!.Presence);
        Assert.Equal(ArtworkHashes.ComputeSha256(PngBytes), identity.ContentSha256);
        Assert.Equal(PngBytes.LongLength, identity.ByteLength);
        Assert.Equal(500, identity.Width);
        Assert.Equal(750, identity.Height);
        Assert.Equal(Modified, identity.DateModifiedUtc);
        Assert.Equal("tag-1", identity.JellyfinImageTag);
    }

    [Fact]
    public async Task PresentJpegIsConfined()
    {
        var reader = Reader(() => ArtworkImageAccessResult.Present(JpegBytes, 400, 600, SourceOrientation.TopLeft, Modified, null));

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Present, result.Status);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Theory]
    [InlineData(SourceOrientation.RightTop, 750, 500)]
    [InlineData(SourceOrientation.LeftBottom, 750, 500)]
    [InlineData(SourceOrientation.BottomRight, 500, 750)]
    [InlineData(SourceOrientation.TopLeft, 500, 750)]
    public async Task OrientedDimensionsAreSuppliedForTheRenderer(
        SourceOrientation orientation,
        int expectedWidth,
        int expectedHeight)
    {
        var reader = Reader(() => ArtworkImageAccessResult.Present(PngBytes, 500, 750, orientation, Modified, null));

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Present, result.Status);
        Assert.Equal(expectedWidth, result.OrientedWidth);
        Assert.Equal(expectedHeight, result.OrientedHeight);
        Assert.True(result.TryCreateSourceImageInput(out var input));
        Assert.Equal(expectedWidth, input!.OrientedWidth);
        Assert.Equal(expectedHeight, input.OrientedHeight);
    }

    // ---- Absence ---------------------------------------------------------------

    [Fact]
    public async Task AbsentSurfaceProducesAnExplicitAbsentResultAndIdentity()
    {
        var reader = Reader(() => ArtworkImageAccessResult.Absent());

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Absent, result.Status);
        Assert.Equal(0, result.ByteLength);
        Assert.Null(result.ContentType);
        Assert.Null(result.ContentSha256);
        Assert.False(result.TryCreateSourceImageInput(out var input));
        Assert.Null(input);

        Assert.True(result.TryCreateActiveImageIdentity(out var identity));
        Assert.NotNull(identity);
        Assert.Equal(ArtworkImagePresence.Absent, identity!.Presence);
        Assert.Null(identity.ContentSha256);
    }

    // ---- Failure ---------------------------------------------------------------

    [Fact]
    public async Task FailedAccessMapsToABoundedUnreadableResult()
    {
        var reader = Reader(() => ArtworkImageAccessResult.Failed(ArtworkImageAccessFailure.Unreadable, "host detail that must not leak"));

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.Unreadable, result.FailureReason);
        Assert.Empty(result.Bytes.ToArray());
        Assert.False(result.TryCreateSourceImageInput(out _));
        Assert.False(result.TryCreateActiveImageIdentity(out _));
    }

    [Fact]
    public async Task UnknownItemMapsToItemNotFound()
    {
        var reader = Reader(() => ArtworkImageAccessResult.Failed(ArtworkImageAccessFailure.ItemNotFound, "not found"));

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.ItemNotFound, result.FailureReason);
    }

    [Fact]
    public async Task AccessExceptionIsBoundedAndNeverEscapes()
    {
        var access = new FakeAccess { Throw = true };
        var reader = new ArtworkSourceReader(access, new OperationalLimits());

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.Unreadable, result.FailureReason);
    }

    [Theory]
    [InlineData(ArtworkImageAccessFailure.ItemNotFound, ArtworkSourceReadFailureReason.ItemNotFound)]
    [InlineData(ArtworkImageAccessFailure.UnsupportedSurface, ArtworkSourceReadFailureReason.UnsupportedSurface)]
    [InlineData(ArtworkImageAccessFailure.UnsupportedContentType, ArtworkSourceReadFailureReason.UnsupportedContentType)]
    [InlineData(ArtworkImageAccessFailure.SourceTooLarge, ArtworkSourceReadFailureReason.SourceTooLarge)]
    [InlineData(ArtworkImageAccessFailure.Unreadable, ArtworkSourceReadFailureReason.Unreadable)]
    public async Task AccessFailuresMapToBoundedReaderReasons(
        ArtworkImageAccessFailure accessFailure,
        ArtworkSourceReadFailureReason expected)
    {
        var reader = Reader(() => ArtworkImageAccessResult.Failed(accessFailure, "bounded"));

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(expected, result.FailureReason);
    }

    // ---- Surface and identifier validation -------------------------------------

    [Fact]
    public async Task IndexedSurfaceIsRejectedWithoutAccessingTheHost()
    {
        var access = new FakeAccess();
        var reader = new ArtworkSourceReader(access, new OperationalLimits());

        var result = await reader.ReadAsync(Item, IndexedSurface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.UnsupportedSurface, result.FailureReason);
        Assert.Equal(0, access.Calls);
    }

    [Fact]
    public async Task EmptyItemIdentifierIsRejectedWithoutAccessingTheHost()
    {
        var access = new FakeAccess();
        var reader = new ArtworkSourceReader(access, new OperationalLimits());

        var result = await reader.ReadAsync(Guid.Empty, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.ItemNotFound, result.FailureReason);
        Assert.Equal(0, access.Calls);
    }

    // ---- Content-type confinement ----------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 })]
    [InlineData(new byte[] { 0x42, 0x4D })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 })]
    [InlineData(new byte[] { 0x01, 0x02, 0x03, 0x04 })]
    public async Task UninspectedContainersFailClosed(byte[] bytes)
    {
        var reader = Reader(() => ArtworkImageAccessResult.Present(bytes, 100, 150, SourceOrientation.TopLeft, Modified, null));

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.UnsupportedContentType, result.FailureReason);
        Assert.False(result.TryCreateSourceImageInput(out _));
    }

    // ---- Bounds ----------------------------------------------------------------

    [Fact]
    public async Task OversizedBytesFailClosed()
    {
        var limits = new OperationalLimits { SourceArtifactLimitBytes = 4 };
        var access = new FakeAccess
        {
            Result = (_, _) => ArtworkImageAccessResult.Present(PngBytes, 100, 150, SourceOrientation.TopLeft, Modified, null),
        };
        var reader = new ArtworkSourceReader(access, limits);

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.SourceTooLarge, result.FailureReason);
    }

    [Fact]
    public async Task OversizedEncodedDimensionsFailClosed()
    {
        var limits = new OperationalLimits { MaxImageDimensionPixels = 1024 };
        var access = new FakeAccess
        {
            Result = (_, _) => ArtworkImageAccessResult.Present(PngBytes, 1025, 100, SourceOrientation.TopLeft, Modified, null),
        };
        var reader = new ArtworkSourceReader(access, limits);

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.DimensionTooLarge, result.FailureReason);
    }

    [Fact]
    public async Task SwappedDisplayDimensionBeyondTheLimitFailsClosed()
    {
        var limits = new OperationalLimits { MaxImageDimensionPixels = 1024 };
        var access = new FakeAccess
        {
            // The encoded height becomes the display width after the swap.
            Result = (_, _) => ArtworkImageAccessResult.Present(PngBytes, 512, 1025, SourceOrientation.RightTop, Modified, null),
        };
        var reader = new ArtworkSourceReader(access, limits);

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Failed, result.Status);
        Assert.Equal(ArtworkSourceReadFailureReason.DimensionTooLarge, result.FailureReason);
    }

    // ---- Boundary hygiene ------------------------------------------------------

    [Fact]
    public void TheReadResultAndRendererInputExposeNoPathOrJellyfinEntity()
    {
        AssertNoHostTypeLeak(typeof(ArtworkSourceReadResult));
        AssertNoHostTypeLeak(typeof(SourceImageInput));
        AssertNoHostTypeLeak(typeof(ArtworkImageAccessResult));
        AssertNoHostTypeLeak(typeof(IArtworkSourceReader));
        AssertNoHostTypeLeak(typeof(IArtworkImageAccess));
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

    private static ArtworkSourceReader Reader(Func<ArtworkImageAccessResult> result)
    {
        var access = new FakeAccess { Result = (_, _) => result() };
        return new ArtworkSourceReader(access, new OperationalLimits());
    }

    private sealed class FakeAccess : IArtworkImageAccess
    {
        public Func<Guid, ArtworkImageSurface, ArtworkImageAccessResult>? Result { get; set; }

        public bool Throw { get; set; }

        public int Calls { get; private set; }

        public Task<ArtworkImageAccessResult> AccessAsync(
            Guid itemId,
            ArtworkImageSurface surface,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("The fake host boundary failed.");
            }

            return Task.FromResult(Result!(itemId, surface));
        }
    }
}
