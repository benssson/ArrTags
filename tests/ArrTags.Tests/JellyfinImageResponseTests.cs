using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Artwork;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.11 integration checks for the supported standard Jellyfin
/// server image response path. The pinned <c>Jellyfin.Api.dll</c> host
/// <c>ImageController</c> is loaded and invoked in-process with the real route
/// and response pipeline against <see cref="DispatchProxy"/> host doubles and a
/// real <see cref="DefaultHttpContext"/>. This exercises the same controller
/// actions, tags, conditional/cache headers, size/format plumbing, and non-200
/// pass-through that an image-consuming client receives from the standard route,
/// without a running host and without mutating a real library. The
/// <see cref="JellyfinHostFactAttribute"/> guard skips the facts when
/// <c>ARRTAGS_JELLYFIN_HOST_DIR</c> is unset and fails when it is set but the
/// pinned host assembly cannot be loaded or its contracts do not match.
/// </summary>
public class JellyfinImageResponseTests
{
    private static readonly Guid Item = Guid.Parse("12345678-1234-1234-1234-123456789abc");

    // ---- Standard unindexed Primary response -----------------------------------

    [JellyfinHostFact]
    public async Task UnindexedPrimaryRouteServesTheServerRenderedRepresentationWithTagCacheHeaders()
    {
        using var harness = new RouteHarness();
        harness.LibraryStub.Item = _ => harness.Movie;

        var result = await harness.GetItemImageAsync(Item, ImageType.Primary, tag: "etag-1", imageIndex: 0);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal(harness.ServedPath, file.FileName);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("image/png", harness.Response.ContentType);
        Assert.Equal("attachment", harness.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("\"etag-1\"", harness.Response.Headers.ETag.ToString());
        Assert.Equal("public, max-age=31536000, immutable", harness.Response.Headers.CacheControl.ToString());
        Assert.Equal(
            harness.ProcessorStub.DateModified.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss \"GMT\"", CultureInfo.InvariantCulture),
            harness.Response.Headers.LastModified.ToString());
        Assert.Equal("Accept", harness.Response.Headers.Vary.ToString());
        Assert.Equal("Interactive", harness.Response.Headers["transferMode.dlna.org"].ToString());
        Assert.Equal("DLNA.ORG_TLAG=*", harness.Response.Headers["realTimeInfo.dlna.org"].ToString());
        Assert.False(string.IsNullOrEmpty(harness.Response.Headers.Age.ToString()));

        // The route asked the image processor for the item's resolved image and
        // served the representation it returned; it does not invent a parallel
        // path or write a response itself.
        Assert.Equal(harness.Movie.GetImageInfo(ImageType.Primary, 0), harness.ProcessorStub.LastOptions!.Image);
    }

    [JellyfinHostFact]
    public async Task IndexedRouteServesTheSameServerRenderedRepresentationAndMissingIndexIs404()
    {
        using var harness = new RouteHarness();
        harness.LibraryStub.Item = _ => harness.Movie;

        var present = await harness.GetItemImageByIndexAsync(Item, ImageType.Primary, imageIndex: 0, tag: "etag-1");
        var indexed = Assert.IsType<PhysicalFileResult>(present);
        Assert.Equal(harness.ServedPath, indexed.FileName);
        Assert.Equal("image/png", indexed.ContentType);

        var calls = harness.ProcessorStub.Calls;
        var missing = await harness.GetItemImageByIndexAsync(Item, ImageType.Primary, imageIndex: 3, tag: "etag-1");
        Assert.IsType<NotFoundObjectResult>(missing);
        Assert.Equal(calls, harness.ProcessorStub.Calls);
    }

    [JellyfinHostFact]
    public async Task PathFormRouteServesTheSameRepresentation()
    {
        using var harness = new RouteHarness();
        harness.LibraryStub.Item = _ => harness.Movie;

        var present = await harness.GetItemImage2Async(
            Item,
            ImageType.Primary,
            imageIndex: 0,
            tag: "etag-1",
            format: ImageFormat.Png,
            maxWidth: 400,
            maxHeight: 600);

        var file = Assert.IsType<PhysicalFileResult>(present);
        Assert.Equal(harness.ServedPath, file.FileName);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("\"etag-1\"", harness.Response.Headers.ETag.ToString());
        Assert.Equal(400, harness.ProcessorStub.LastOptions!.MaxWidth);
        Assert.Equal(600, harness.ProcessorStub.LastOptions.MaxHeight);
    }

    // ---- Conditional requests / cache behavior ---------------------------------

    [JellyfinHostFact]
    public async Task MatchingImageTagReturnsNotModifiedForQuotedAndBareIfNoneMatch()
    {
        using var harness = new RouteHarness();
        harness.LibraryStub.Item = _ => harness.Movie;

        foreach (var value in new[] { "\"etag-1\"", "etag-1" })
        {
            harness.Reset();
            harness.Request.Headers.IfNoneMatch = value;

            var result = await harness.GetItemImageAsync(Item, ImageType.Primary, tag: "etag-1", imageIndex: 0);

            Assert.IsType<ContentResult>(result);
            Assert.Equal(StatusCodes.Status304NotModified, harness.Response.StatusCode);
        }
    }

    [JellyfinHostFact]
    public async Task WithoutTagTheRouteUsesTheTimeBasedCacheCondition()
    {
        using var harness = new RouteHarness();
        harness.LibraryStub.Item = _ => harness.Movie;
        harness.ProcessorStub.DateModified = DateTime.UtcNow.AddDays(-1);
        harness.Request.Headers.IfModifiedSince = DateTime.UtcNow.ToString("R");

        var result = await harness.GetItemImageAsync(Item, ImageType.Primary, tag: null, imageIndex: 0);

        Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, harness.Response.StatusCode);
    }

    [JellyfinHostFact]
    public async Task NoCacheRequestForcesRevalidationAndNoStore()
    {
        using var harness = new RouteHarness();
        harness.LibraryStub.Item = _ => harness.Movie;
        harness.Request.Headers.CacheControl = "no-cache";

        var result = await harness.GetItemImageAsync(Item, ImageType.Primary, tag: "etag-1", imageIndex: 0);

        Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("no-cache, no-store, must-revalidate", harness.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache, no-store, must-revalidate", harness.Response.Headers.Pragma.ToString());
    }

    // ---- Requested size/format and non-200 pass-through ------------------------

    [JellyfinHostFact]
    public async Task RequestedSizeAndFormatAreHandedToTheImageProcessor()
    {
        using var harness = new RouteHarness();
        harness.LibraryStub.Item = _ => harness.Movie;

        await harness.GetItemImageAsync(
            Item,
            ImageType.Primary,
            tag: "etag-1",
            imageIndex: 0,
            maxWidth: 320,
            maxHeight: 480,
            width: 160,
            height: 240,
            quality: 80,
            format: ImageFormat.Png);

        var options = harness.ProcessorStub.LastOptions!;
        Assert.Equal(320, options.MaxWidth);
        Assert.Equal(480, options.MaxHeight);
        Assert.Equal(160, options.Width);
        Assert.Equal(240, options.Height);
        Assert.Equal(80, options.Quality);
        Assert.Equal(new[] { ImageFormat.Png }, options.SupportedOutputFormats);
    }

    [JellyfinHostFact]
    public async Task UnknownItemAndItemWithoutImageArePassedThroughAs404()
    {
        using var harness = new RouteHarness();

        harness.LibraryStub.Item = _ => null;
        var unknown = await harness.GetItemImageAsync(Item, ImageType.Primary, tag: "etag-1", imageIndex: 0);
        Assert.IsType<NotFoundResult>(unknown);
        Assert.Equal(0, harness.ProcessorStub.Calls);

        harness.LibraryStub.Item = _ => new TestMovie();
        var absent = await harness.GetItemImageAsync(Item, ImageType.Primary, tag: "etag-1", imageIndex: 0);
        Assert.IsType<NotFoundObjectResult>(absent);
        Assert.Equal(0, harness.ProcessorStub.Calls);
    }

    // ---- Publication through the supported API and standard-route read-back -----

    [JellyfinHostFact]
    public async Task PublishedDerivedImageIsServedByTheStandardRouteInsteadOfTheStaleSource()
    {
        using var harness = new RouteHarness();
        var sourceBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x01, 0x01, 0x01 };
        var derivedBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x02, 0x02, 0x02, 0x02, 0x02 };
        var source = harness.SeedPrimary(sourceBytes);
        harness.LibraryStub.Item = _ => harness.Movie;

        // Publish the derived bytes through the real ArrTags mutation boundary.
        // The provider double mirrors the supported host ImageSaver flow: it
        // writes the plugin-owned stream and updates the item's image identity.
        string? derivedPath = null;
        harness.ProviderStub.OnStreamSave = (item, bytes, mimeType, imageType, imageIndex) =>
        {
            derivedPath = Path.Combine(Path.GetTempPath(), "arrtags-derived-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(derivedPath, bytes);
            item.ImageInfos =
            [
                new ItemImageInfo
                {
                    Path = derivedPath,
                    Type = ImageType.Primary,
                    DateModified = DateTime.UtcNow,
                },
            ];
        };

        var writer = new JellyfinArtworkImageWriter(harness.Library, harness.Provider);
        var save = await writer.SaveImageAsync(Item, ArtworkImageSurface.Primary, derivedBytes, "image/png", CancellationToken.None);
        var update = await writer.PersistItemUpdateAsync(Item, ArtworkImageSurface.Primary, CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.True(update.Succeeded);
        Assert.NotNull(derivedPath);
        Assert.NotEqual(source, derivedPath);

        // The original source file is retained byte-for-byte; ArrTags wrote a new
        // derived artifact through the supported API instead of mutating the source.
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(source));

        // The standard route now serves the derived representation, not the stale
        // source the item held before publication.
        var result = await harness.GetItemImageAsync(Item, ImageType.Primary, tag: "etag-derived", imageIndex: 0);
        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal(derivedPath, file.FileName);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(derivedBytes, await File.ReadAllBytesAsync(file.FileName));
        Assert.NotEqual(sourceBytes, await File.ReadAllBytesAsync(file.FileName));

        try
        {
            File.Delete(derivedPath!);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---- In-process controller harness -----------------------------------------

    private sealed class RouteHarness : IDisposable
    {
        private readonly Type _controllerType;
        private readonly object _controller;

        public RouteHarness()
        {
            _controllerType = HostControllerType.Value;

            ServedPath = Path.GetTempFileName();
            File.WriteAllBytes(ServedPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]);
            Movie = new TestMovie();
            Movie.ImageInfos =
            [
                new ItemImageInfo
                {
                    Path = ServedPath,
                    Type = ImageType.Primary,
                    DateModified = DateTime.UtcNow.AddMinutes(-5),
                },
            ];

            Library = DispatchProxy.Create<ILibraryManager, StubLibraryManager>();
            LibraryStub = (StubLibraryManager)(object)Library;

            Processor = DispatchProxy.Create<IImageProcessor, StubImageProcessor>();
            ProcessorStub = (StubImageProcessor)(object)Processor;

            Provider = DispatchProxy.Create<IProviderManager, StubProviderManager>();
            ProviderStub = (StubProviderManager)(object)Provider;

            Context = new DefaultHttpContext();
            Request = Context.Request;

            _controller = CreateController(_controllerType, Library, Provider, Processor, Context);
        }

        public string ServedPath { get; }

        public TestMovie Movie { get; }

        public ILibraryManager Library { get; }

        public StubLibraryManager LibraryStub { get; }

        public IProviderManager Provider { get; }

        public StubProviderManager ProviderStub { get; }

        public IImageProcessor Processor { get; }

        public StubImageProcessor ProcessorStub { get; }

        public DefaultHttpContext Context { get; }

        public HttpRequest Request { get; }

        public HttpResponse Response => Context.Response;

        public void Reset()
        {
            Request.Headers.Clear();
            Context.Response.Headers.Clear();
            Context.Response.StatusCode = StatusCodes.Status200OK;
        }

        public string SeedPrimary(byte[] bytes)
        {
            var path = Path.Combine(Path.GetTempPath(), "arrtags-source-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, bytes);
            Movie.ImageInfos =
            [
                new ItemImageInfo
                {
                    Path = path,
                    Type = ImageType.Primary,
                    DateModified = DateTime.UtcNow.AddMinutes(-5),
                },
            ];
            ServedPathOverride = path;
            return path;
        }

        public string? ServedPathOverride { get; private set; }

        public Task<ActionResult> GetItemImageAsync(
            Guid itemId,
            ImageType imageType,
            string? tag,
            int? imageIndex,
            int? maxWidth = null,
            int? maxHeight = null,
            int? width = null,
            int? height = null,
            int? quality = null,
            ImageFormat? format = null)
        {
            var method = _controllerType.GetMethod("GetItemImage", BindingFlags.Public | BindingFlags.Instance)!;
            var args = new object?[]
            {
                itemId,
                imageType,
                maxWidth,
                maxHeight,
                width,
                height,
                quality,
                null,
                null,
                tag,
                format,
                null,
                null,
                null,
                null,
                null,
                imageIndex,
            };

            return InvokeAsync(method, args);
        }

        public Task<ActionResult> GetItemImageByIndexAsync(
            Guid itemId,
            ImageType imageType,
            int imageIndex,
            string? tag)
        {
            var method = _controllerType.GetMethod("GetItemImageByIndex", BindingFlags.Public | BindingFlags.Instance)!;
            var args = new object?[]
            {
                itemId,
                imageType,
                imageIndex,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                tag,
                null,
                null,
                null,
                null,
                null,
                null,
            };

            return InvokeAsync(method, args);
        }

        public Task<ActionResult> GetItemImage2Async(
            Guid itemId,
            ImageType imageType,
            int imageIndex,
            string tag,
            ImageFormat format,
            int maxWidth,
            int maxHeight)
        {
            var method = _controllerType.GetMethod("GetItemImage2", BindingFlags.Public | BindingFlags.Instance)!;
            var args = new object?[]
            {
                itemId,
                imageType,
                maxWidth,
                maxHeight,
                null,
                null,
                null,
                null,
                null,
                tag,
                format,
                0.0d,
                0,
                null,
                null,
                null,
                imageIndex,
            };

            return InvokeAsync(method, args);
        }

        public void Dispose()
        {
            foreach (var path in new[] { ServedPath, ServedPathOverride }.Where(p => !string.IsNullOrEmpty(p)).Distinct())
            {
                try
                {
                    File.Delete(path!);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private async Task<ActionResult> InvokeAsync(MethodInfo method, object?[] args)
        {
            var task = (Task<ActionResult>)method.Invoke(_controller, args)!;
            return await task.ConfigureAwait(false);
        }
    }

    private static object CreateController(
        Type controllerType,
        ILibraryManager library,
        IProviderManager provider,
        IImageProcessor processor,
        HttpContext httpContext)
    {
        var constructor = controllerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => DependencyFor(parameter.ParameterType, library, provider, processor))
            .ToArray();

        var controller = constructor.Invoke(arguments);
        controllerType.GetProperty("ControllerContext")!.SetValue(
            controller,
            new ControllerContext { HttpContext = httpContext });
        return controller;
    }

    private static object DependencyFor(
        Type parameterType,
        ILibraryManager library,
        IProviderManager provider,
        IImageProcessor processor)
    {
        var value = parameterType.FullName switch
        {
            "MediaBrowser.Controller.Library.IUserManager" => CreateThrowingProxy(parameterType),
            "MediaBrowser.Controller.Library.ILibraryManager" => library,
            "MediaBrowser.Controller.Providers.IProviderManager" => provider,
            "MediaBrowser.Controller.Drawing.IImageProcessor" => processor,
            "MediaBrowser.Model.IO.IFileSystem" => CreateThrowingProxy(parameterType),
            "MediaBrowser.Controller.Configuration.IServerConfigurationManager" => CreateThrowingProxy(parameterType),
            "MediaBrowser.Common.Configuration.IApplicationPaths" => CreateThrowingProxy(parameterType),
            _ when parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ILogger<>) => NullLoggerFor(parameterType),
            _ => throw new InvalidOperationException($"Unexpected ImageController dependency '{parameterType.FullName}'."),
        };

        Assert.True(
            parameterType.IsInstanceOfType(value),
            $"ImageController dependency '{parameterType.FullName}' is not the pinned host contract; Jellyfin.Api.dll resolved a different copy of its interface assembly.");
        return value;
    }

    private static object CreateThrowingProxy(Type interfaceType)
    {
        return DispatchProxy.Create(interfaceType, typeof(ThrowingDispatchProxy));
    }

    private static object NullLoggerFor(Type loggerType)
    {
        var implementation = typeof(NullLogger<>).MakeGenericType(loggerType.GetGenericArguments()[0]);
        var instance = implementation
            .GetField("Instance", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);
        return instance ?? Activator.CreateInstance(implementation)!;
    }

    private static readonly Lazy<Type> HostControllerType = new(() =>
    {
        var hostDirectory = Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR");
        Assert.False(string.IsNullOrWhiteSpace(hostDirectory), "ARRTAGS_JELLYFIN_HOST_DIR must be set for this fact.");

        var apiPath = Path.Combine(hostDirectory!, "Jellyfin.Api.dll");
        Assert.True(File.Exists(apiPath), $"Expected the pinned host Jellyfin.Api.dll at {apiPath}.");

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(hostDirectory!, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(apiPath);
        var controllerType = assembly.GetType("Jellyfin.Api.Controllers.ImageController", throwOnError: false);
        Assert.NotNull(controllerType);
        return controllerType!;
    });

    // ---- Host interface doubles ------------------------------------------------

    public class ThrowingDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException(targetMethod?.Name);
    }

    public class StubLibraryManager : DispatchProxy
    {
        public Func<Guid, BaseItem?>? Item { get; set; }

        public Func<BaseItem, ItemImageInfo, int, bool, Task<ItemImageInfo>>? Convert { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (targetMethod.Name == nameof(ILibraryManager.GetItemById))
            {
                return Item?.Invoke((Guid)args![0]!);
            }

            if (targetMethod.Name == nameof(ILibraryManager.ConvertImageToLocal))
            {
                if (Convert is null)
                {
                    return Task.FromResult((ItemImageInfo)args![1]!);
                }

                return Convert(
                    (BaseItem)args![0]!,
                    (ItemImageInfo)args[1]!,
                    (int)args[2]!,
                    args.Length > 3 && (bool)args[3]!);
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    public class StubImageProcessor : DispatchProxy
    {
        public int Calls { get; private set; }

        public ImageProcessingOptions? LastOptions { get; private set; }

        public DateTime DateModified { get; set; } = DateTime.UtcNow.AddMinutes(-5);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IImageProcessor.ProcessImage))
            {
                Calls++;
                var options = (ImageProcessingOptions)args![0]!;
                LastOptions = options;
                return Task.FromResult<(string Path, string? MimeType, DateTime DateModified)>(
                    (options.Image.Path, "image/png", DateModified));
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class StubProviderManager : DispatchProxy
    {
        public Action<BaseItem, byte[], string, ImageType, int?>? OnStreamSave { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IProviderManager.SaveImage) && args![1] is Stream stream)
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                OnStreamSave?.Invoke(
                    (BaseItem)args[0]!,
                    buffer.ToArray(),
                    (string)args[2]!,
                    (ImageType)args[3]!,
                    (int?)args[4]);
                return Task.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class TestMovie : Movie
    {
        public override Task UpdateToRepositoryAsync(ItemUpdateType updateReason, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

/// <summary>
/// Unguarded Phase 5 task 5.11 checks for the pinned Jellyfin 12.0.0 requested
/// size behavior that the standard route relies on. The controller hands the
/// requested dimensions to <see cref="IImageProcessor"/>; the pinned core helper
/// clamps the result to the source so a client request can never upscale the
/// published artwork. These run against the pinned NuGet assemblies in the
/// default suite, so no host directory is required.
/// </summary>
public class JellyfinImageResizeContractTests
{
    [Fact]
    public void RequestedSizeLargerThanTheSourceNeverUpscales()
    {
        var source = new ImageDimensions(600, 336);

        // A bounding request larger than the source is clamped to the source; a
        // fixed or fill request may reshape the box but still cannot exceed the
        // source dimensions.
        Assert.Equal(source, ImageHelper.GetNewImageSize(new ImageProcessingOptions { MaxWidth = 2000, MaxHeight = 2000 }, source));

        var fixedSize = ImageHelper.GetNewImageSize(new ImageProcessingOptions { Width = 2000, Height = 2000 }, source);
        Assert.True(fixedSize.Width <= source.Width);
        Assert.True(fixedSize.Height <= source.Height);

        var filled = ImageHelper.GetNewImageSize(new ImageProcessingOptions { FillWidth = 2000, FillHeight = 2000 }, source);
        Assert.True(filled.Width <= source.Width);
        Assert.True(filled.Height <= source.Height);
    }

    [Fact]
    public void SmallerRequestedSizeStillDownscalesWithinTheSource()
    {
        var source = new ImageDimensions(600, 336);

        var bounded = ImageHelper.GetNewImageSize(new ImageProcessingOptions { MaxWidth = 300, MaxHeight = 300 }, source);

        Assert.True(bounded.Width <= 300);
        Assert.True(bounded.Height <= 300);
        Assert.True(bounded.Width < source.Width);
    }
}
