using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 5 task 5.1 confirmation of the pinned Jellyfin 12.0.0 item-image
/// publication and read ABI. These tests reflect over the pinned
/// <c>Jellyfin.Controller</c> / <c>Jellyfin.Model</c> <c>12.0.0</c> assemblies
/// already referenced by the test project, so they run in the default suite with
/// no live host. They pin the exact type locations, method signatures, and enum
/// values that the later Phase 5 publication, source-capture, and verification
/// tasks rely on; the corresponding route/authorization confirmation is
/// <see cref="JellyfinImageRouteTests"/>.
/// </summary>
public class JellyfinImageAbiTests
{
    [Fact]
    public void PinnedControllerAndModelAssembliesAreVersion12_0_0()
    {
        Assert.Equal(new Version(12, 0, 0, 0), typeof(IProviderManager).Assembly.GetName().Version);
        Assert.Equal("MediaBrowser.Controller", typeof(IProviderManager).Assembly.GetName().Name);
        Assert.Equal(new Version(12, 0, 0, 0), typeof(ImageType).Assembly.GetName().Version);
        Assert.Equal("MediaBrowser.Model", typeof(ImageType).Assembly.GetName().Name);
    }

    [Fact]
    public void ImageTypeIsPinnedToTheModelEnumWithPrimaryZero()
    {
        Assert.Equal("MediaBrowser.Model.Entities", typeof(ImageType).Namespace);
        Assert.True(typeof(ImageType).IsEnum);
        Assert.Equal(0, (int)ImageType.Primary);
        Assert.Equal(5, (int)ImageType.Thumb);
        Assert.Equal(2, (int)ImageType.Backdrop);
    }

    [Fact]
    public void ImageFormatIsPinnedToTheModelEnum()
    {
        Assert.Equal("MediaBrowser.Model.Drawing", typeof(ImageFormat).Namespace);
        Assert.True(typeof(ImageFormat).IsEnum);
        Assert.Contains(ImageFormat.Jpg, Enum.GetValues<ImageFormat>());
        Assert.Contains(ImageFormat.Png, Enum.GetValues<ImageFormat>());
        Assert.Contains(ImageFormat.Webp, Enum.GetValues<ImageFormat>());
        Assert.Contains(ImageFormat.Gif, Enum.GetValues<ImageFormat>());
    }

    [Fact]
    public void ProviderManagerSaveImageStreamOverloadIsPinned()
    {
        // The V1 publication call: byte stream + MIME type + image type + index.
        var method = FindMethod(
            typeof(IProviderManager),
            "SaveImage",
            typeof(BaseItem),
            typeof(Stream),
            typeof(string),
            typeof(ImageType),
            typeof(int?),
            typeof(CancellationToken));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        Assert.False(method.IsGenericMethod);
    }

    [Fact]
    public void ProviderManagerSaveImageUrlAndPathOverloadsArePinned()
    {
        var urlOverload = FindMethod(
            typeof(IProviderManager),
            "SaveImage",
            typeof(BaseItem),
            typeof(string),
            typeof(ImageType),
            typeof(int?),
            typeof(CancellationToken));
        Assert.NotNull(urlOverload);
        Assert.Equal(typeof(Task), urlOverload!.ReturnType);

        var pathOverload = FindMethod(
            typeof(IProviderManager),
            "SaveImage",
            typeof(BaseItem),
            typeof(string),
            typeof(string),
            typeof(ImageType),
            typeof(int?),
            typeof(bool?),
            typeof(CancellationToken));
        Assert.NotNull(pathOverload);
        Assert.Equal(typeof(Task), pathOverload!.ReturnType);
    }

    [Fact]
    public void ItemImageInfoCarriesPathTypeDateDimensionsBlurHashAndLocalFlag()
    {
        Assert.Equal("MediaBrowser.Controller.Entities", typeof(ItemImageInfo).Namespace);

        Assert.Equal(typeof(string), GetProperty<ItemImageInfo>("Path").PropertyType);
        Assert.Equal(typeof(ImageType), GetProperty<ItemImageInfo>("Type").PropertyType);
        Assert.Equal(typeof(DateTime), GetProperty<ItemImageInfo>("DateModified").PropertyType);
        Assert.Equal(typeof(int), GetProperty<ItemImageInfo>("Width").PropertyType);
        Assert.Equal(typeof(int), GetProperty<ItemImageInfo>("Height").PropertyType);
        Assert.Equal(typeof(string), GetProperty<ItemImageInfo>("BlurHash").PropertyType);
        Assert.Equal(typeof(bool), GetProperty<ItemImageInfo>("IsLocalFile").PropertyType);
    }

    [Fact]
    public void ImageInfoCarriesTypeIndexTagPathDimensionsAndSize()
    {
        Assert.Equal("MediaBrowser.Model.Dto", typeof(ImageInfo).Namespace);

        Assert.Equal(typeof(ImageType), GetProperty<ImageInfo>("ImageType").PropertyType);
        Assert.Equal(typeof(int?), GetProperty<ImageInfo>("ImageIndex").PropertyType);
        Assert.Equal(typeof(string), GetProperty<ImageInfo>("ImageTag").PropertyType);
        Assert.Equal(typeof(string), GetProperty<ImageInfo>("Path").PropertyType);
        Assert.Equal(typeof(int?), GetProperty<ImageInfo>("Width").PropertyType);
        Assert.Equal(typeof(int?), GetProperty<ImageInfo>("Height").PropertyType);
        Assert.Equal(typeof(long), GetProperty<ImageInfo>("Size").PropertyType);
    }

    [Fact]
    public void BaseItemImageReadAndRepositorySurfaceIsPinned()
    {
        Assert.Equal(typeof(ItemImageInfo), FindMethod(typeof(BaseItem), "GetImageInfo", typeof(ImageType), typeof(int))!.ReturnType);
        Assert.Equal(typeof(string), FindMethod(typeof(BaseItem), "GetImagePath", typeof(ImageType), typeof(int))!.ReturnType);
        Assert.Equal(typeof(bool), FindMethod(typeof(BaseItem), "HasImage", typeof(ImageType), typeof(int))!.ReturnType);
        Assert.Equal(typeof(bool), FindMethod(typeof(BaseItem), "AllowsMultipleImages", typeof(ImageType))!.ReturnType);
        Assert.Equal(typeof(ItemImageInfo[]), GetProperty<BaseItem>("ImageInfos").PropertyType);

        Assert.Equal(
            typeof(Task),
            FindMethod(typeof(BaseItem), "UpdateToRepositoryAsync", typeof(ItemUpdateType), typeof(CancellationToken))!.ReturnType);
        Assert.Equal(
            typeof(Task),
            FindMethod(typeof(BaseItem), "DeleteImageAsync", typeof(ImageType), typeof(int))!.ReturnType);

        // V1 publishes only an unindexed Primary poster; multiple images are for backdrops/chapters.
        Assert.NotNull(typeof(BaseItem).GetMethod("SetImagePath", [typeof(ImageType), typeof(int), typeof(MediaBrowser.Model.IO.FileSystemMetadata)]));
    }

    [Fact]
    public void ItemUpdateTypeImageUpdateFlagIsPinned()
    {
        Assert.Equal("MediaBrowser.Controller.Library", typeof(ItemUpdateType).Namespace);
        Assert.True(typeof(ItemUpdateType).IsEnum);
        Assert.Equal(4, (int)ItemUpdateType.ImageUpdate);
    }

    [Fact]
    public void LibraryManagerImageReadAndConversionSurfaceIsPinned()
    {
        var updateImages = FindMethod(typeof(ILibraryManager), "UpdateImagesAsync", typeof(BaseItem), typeof(bool));
        Assert.NotNull(updateImages);
        Assert.Equal(typeof(Task), updateImages!.ReturnType);

        var convert = FindMethod(typeof(ILibraryManager), "ConvertImageToLocal", typeof(BaseItem), typeof(ItemImageInfo), typeof(int), typeof(bool));
        Assert.NotNull(convert);
        Assert.Equal(typeof(Task<ItemImageInfo>), convert!.ReturnType);

        // The user-scoped overload the image controller uses to resolve and authorize an item.
        Assert.NotNull(FindMethod(typeof(ILibraryManager), "GetItemById", typeof(Guid), typeof(Guid)));
    }

    [Fact]
    public void ImageProcessorProcessImageSignatureIsPinned()
    {
        var processImage = FindMethod(typeof(IImageProcessor), "ProcessImage", typeof(ImageProcessingOptions));
        Assert.NotNull(processImage);
        Assert.Equal(typeof(Task<(string Path, string? MimeType, DateTime DateModified)>), processImage!.ReturnType);

        Assert.NotNull(FindMethod(typeof(IImageProcessor), "GetImageCacheTag", typeof(BaseItem), typeof(ItemImageInfo)));
        Assert.NotNull(FindMethod(typeof(IImageProcessor), "GetImageDimensions", typeof(BaseItem), typeof(ItemImageInfo)));
    }

    private static PropertyInfo GetProperty<T>(string name)
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return property!;
    }

    private static MethodInfo? FindMethod(Type type, string name, params Type[] parameterTypes)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == name
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes));
    }
}
