using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using ArrTags.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.3 checks for the Jellyfin host image access. The Jellyfin
/// manager and image processor are replaced with <see cref="DispatchProxy"/>
/// doubles and the image bytes are supplied through a real temporary file, so
/// these tests exercise the item lookup, absence, bounded read, and supported
/// conversion paths without a live Jellyfin host. The positive decode cases need
/// the pinned native raster runtime and are guarded.
/// </summary>
public sealed class JellyfinArtworkImageAccessTests
{
    private static readonly Guid Item = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly ArtworkImageSurface Surface = ArtworkImageSurface.Primary;

    private static readonly byte[] WebpBytes =
        [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x01, 0x02, 0x03, 0x04];

    // ---- Item and surface resolution -------------------------------------------

    [Fact]
    public void SourceReaderAndImageAccessAreRegisteredAsFactories()
    {
        var services = new ServiceCollection();
        new ArrTags.PluginLifecycle.ArrTagsServiceRegistrator().RegisterServices(services, null!);

        var access = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IArtworkImageAccess));
        Assert.NotNull(access.ImplementationFactory);

        var reader = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IArtworkSourceReader));
        Assert.NotNull(reader.ImplementationFactory);
    }

    [Fact]
    public async Task EmptyIdentifierFailsWithoutALibraryLookup()
    {
        var (access, library, _) = Create();

        var result = await access.AccessAsync(Guid.Empty, Surface, CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Failed, result.Status);
        Assert.Equal(ArtworkImageAccessFailure.ItemNotFound, result.Failure);
        Assert.Equal(0, library.GetItemCalls);
    }

    [Fact]
    public async Task UnknownItemFailsClosed()
    {
        var (access, library, _) = Create();
        library.Items = _ => null;

        var result = await access.AccessAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Failed, result.Status);
        Assert.Equal(ArtworkImageAccessFailure.ItemNotFound, result.Failure);
    }

    [Fact]
    public async Task IndexedSurfaceFailsClosed()
    {
        var (access, _, _) = Create();

        var result = await access.AccessAsync(Item, new ArtworkImageSurface(ArtworkImageType.Primary, 1), CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Failed, result.Status);
        Assert.Equal(ArtworkImageAccessFailure.UnsupportedSurface, result.Failure);
    }

    [Fact]
    public async Task AbsentImageIsReportedAsAbsent()
    {
        var (access, library, _) = Create();
        library.Items = _ => new TestMovie();

        var result = await access.AccessAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Absent, result.Status);
        Assert.Equal(0, result.Bytes.Length);
    }

    // ---- Bounded local reads ---------------------------------------------------

    [Fact]
    public async Task UnsupportedLocalContainerFailsClosedBeforeDecode()
    {
        using var file = new TempFile(WebpBytes);
        var (access, library, _) = Create();
        library.Items = _ => WithImage(file.Path);

        var result = await access.AccessAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Failed, result.Status);
        Assert.Equal(ArtworkImageAccessFailure.UnsupportedContentType, result.Failure);
    }

    [Fact]
    public async Task LocalFileOverTheConfiguredLimitFailsClosed()
    {
        var oversized = new byte[128];
        oversized[0] = 0x89;
        oversized[1] = 0x50;
        using var file = new TempFile(oversized);
        var (access, library, _) = Create(new OperationalLimits { SourceArtifactLimitBytes = 64 });
        library.Items = _ => WithImage(file.Path);

        var result = await access.AccessAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Failed, result.Status);
        Assert.Equal(ArtworkImageAccessFailure.SourceTooLarge, result.Failure);
    }

    [Fact]
    public async Task MissingLocalFileFailsClosed()
    {
        var missing = Path.Combine(Path.GetTempPath(), "arrtags-missing-" + Guid.NewGuid().ToString("N") + ".png");
        var (access, library, _) = Create();
        library.Items = _ => WithImage(missing);

        var result = await access.AccessAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Failed, result.Status);
        Assert.Equal(ArtworkImageAccessFailure.Unreadable, result.Failure);
    }

    [Fact]
    public async Task NonLocalImageIsConvertedToLocalThroughTheSupportedApi()
    {
        using var file = new TempFile(WebpBytes);
        var remote = new ItemImageInfo { Path = "http://example.invalid/poster.jpg", Type = ImageType.Primary };
        var (access, library, _) = Create();
        library.Items = _ => new TestMovie { ImageInfos = [remote] };
        library.Convert = (_, _, _, _) => Task.FromResult(new ItemImageInfo
        {
            Path = file.Path,
            Type = ImageType.Primary,
            DateModified = DateTime.UnixEpoch,
        });

        var result = await access.AccessAsync(Item, Surface, CancellationToken.None);

        Assert.True(library.ConvertCalled);
        // The converted file is an uninspected container, so the read still
        // fails closed rather than handing it to the raster stack.
        Assert.Equal(ArtworkImageAccessStatus.Failed, result.Status);
        Assert.Equal(ArtworkImageAccessFailure.UnsupportedContentType, result.Failure);
    }

    // ---- Native decode (guarded) -----------------------------------------------

    [SkiaNativeFact]
    public async Task PresentLocalPngReturnsBoundedBytesAndDimensions()
    {
        var bytes = RenderImageFixtures.CreateRgbPng(20, 30).Bytes.ToArray();
        using var file = new TempFile(bytes);
        var (access, library, processor) = Create();
        processor.Tag = "tag-png";
        library.Items = _ => WithImage(file.Path);

        var result = await access.AccessAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkImageAccessStatus.Present, result.Status);
        Assert.Equal(20, result.EncodedWidth);
        Assert.Equal(30, result.EncodedHeight);
        Assert.True(result.Bytes.Span.SequenceEqual(bytes));
        Assert.Equal("tag-png", result.JellyfinImageTag);
    }

    [SkiaNativeFact]
    public async Task OrientedJpegFlowsThroughTheReaderAsTrueDisplayDimensions()
    {
        // EXIF orientation 6 rotates 90 degrees clockwise, so the 40x60 encoded
        // image displays as 60x40.
        var bytes = RenderImageFixtures.CreateTwoMarkerOrientedJpeg(40, 60, 6);
        using var file = new TempFile(bytes);
        var (access, library, _) = Create();
        library.Items = _ => WithImage(file.Path);
        var reader = new ArtworkSourceReader(access, new OperationalLimits());

        var result = await reader.ReadAsync(Item, Surface, CancellationToken.None);

        Assert.Equal(ArtworkSourceReadStatus.Present, result.Status);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(60, result.OrientedWidth);
        Assert.Equal(40, result.OrientedHeight);
        Assert.True(result.TryCreateSourceImageInput(out var input));
        Assert.Equal(60, input!.OrientedWidth);
        Assert.Equal(40, input.OrientedHeight);
    }

    private static TestMovie WithImage(string path)
    {
        return new TestMovie
        {
            ImageInfos =
            [
                new ItemImageInfo
                {
                    Path = path,
                    Type = ImageType.Primary,
                    DateModified = DateTime.UnixEpoch,
                },
            ],
        };
    }

    private static (JellyfinArtworkImageAccess Access, FakeLibraryManager Library, FakeImageProcessor Processor) Create(
        OperationalLimits? limits = null)
    {
        var library = DispatchProxy.Create<ILibraryManager, FakeLibraryManager>();
        var libraryFake = (FakeLibraryManager)(object)library;
        var processor = DispatchProxy.Create<IImageProcessor, FakeImageProcessor>();
        var processorFake = (FakeImageProcessor)(object)processor;
        processorFake.Tag = "tag";
        var access = new JellyfinArtworkImageAccess(library, processor, limits ?? new OperationalLimits());
        return (access, libraryFake, processorFake);
    }

    public class FakeLibraryManager : DispatchProxy
    {
        public Func<Guid, BaseItem?>? Items { get; set; }

        public Func<BaseItem, ItemImageInfo, int, bool, Task<ItemImageInfo>>? Convert { get; set; }

        public int GetItemCalls { get; private set; }

        public bool ConvertCalled { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetItemById))
            {
                GetItemCalls++;
                return Items?.Invoke((Guid)args![0]!);
            }

            if (targetMethod.Name == nameof(ILibraryManager.ConvertImageToLocal))
            {
                ConvertCalled = true;
                var image = (ItemImageInfo)args![1]!;
                if (Convert is null)
                {
                    return Task.FromResult(image);
                }

                return Convert(
                    (BaseItem)args[0]!,
                    image,
                    (int)args[2]!,
                    (bool)args[3]!);
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    public class FakeImageProcessor : DispatchProxy
    {
        public string? Tag { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IImageProcessor.GetImageCacheTag))
            {
                return Tag;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class TestMovie : Movie
    {
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(byte[] bytes)
        {
            Path = System.IO.Path.GetTempFileName();
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
