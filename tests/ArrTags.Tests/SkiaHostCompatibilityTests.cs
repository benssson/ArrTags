using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using ArrTags.Rendering;
using SkiaSharp;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Task 4.8 spike checks for the pinned SkiaSharp renderer stack on the pinned
/// Linux runtime. The guarded round-trip test proves that the exact Jellyfin
/// 12.0.0 SkiaSharp 3.119.4 native library can decode source bytes, draw with
/// the bundled DejaVu Sans Bold font loaded from its embedded resource, and
/// encode a non-interlaced 8-bit PNG that decodes back to the expected
/// dimensions.
/// </summary>
public class SkiaHostCompatibilityTests
{
    // A 16x16 RGBA PNG generated once with the pinned SkiaSharp 3.119.4 native
    // library. It is a repository-owned synthetic fixture so the spike does not
    // decode an external or copyrighted image.
    private const string SourcePngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAA3NCSVQICAjb4U/gAAAAMUlEQVQokWOUs4liIAUwkaSaHA0scNaqN+fwqAsTMaKXk0Y10EQDIqbhcUllG0jWAAD3DwR0NWzO/wAAAABJRU5ErkJggg==";

    /// <summary>
    /// Documents the Jellyfin 12 plugin-resolution behavior that task 4.8
    /// measured: <c>PluginLoadContext</c> is constructed from the plugin
    /// directory and wraps <see cref="AssemblyDependencyResolver"/>, which does
    /// not discover a dependency manifest inside that directory. Its
    /// <c>Load</c> therefore returns null and the dependency falls back to the
    /// host's default load context. Jellyfin also loads every DLL in the plugin
    /// directory into the plugin context, which is why a bundled managed copy is
    /// used when one is shipped.
    /// </summary>
    [Fact]
    public void PluginDirectoryStyleResolutionDoesNotDiscoverBundledManagedSkiaSharp()
    {
        var directory = Directory.CreateTempSubdirectory("arrtags-skiasharp-alc-");
        try
        {
            var bundled = typeof(SKBitmap).Assembly.Location;
            Assert.False(string.IsNullOrEmpty(bundled));
            File.Copy(bundled, Path.Combine(directory.FullName, "SkiaSharp.dll"), overwrite: true);

            var resolver = new AssemblyDependencyResolver(directory.FullName);
            var resolved = resolver.ResolveAssemblyToPath(new AssemblyName("SkiaSharp"));

            Assert.Null(resolved);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The task 4.8 decode/draw/encode proof. It only runs when the pinned native
    /// runtime is reachable; see <see cref="SkiaNativeFactAttribute"/>. The
    /// default test environment does not place the transitive
    /// <c>libfontconfig.so.1</c> on the loader path, so <c>./build.sh test</c>
    /// reports this as skipped rather than failing.
    /// </summary>
    [SkiaNativeFact]
    public void PinnedSkiaRuntimeDecodesDrawsAndEncodesARoundTripPng()
    {
        var assembly = typeof(SKBitmap).Assembly;
        Assert.Equal(new Version(3, 119, 0, 0), assembly.GetName().Version);
        Assert.Equal("3.119.4.0", FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion);

        using var fontStream = RenderFontIdentity.BundledDejaVuSansBold.OpenResourceStream();
        using var fontBuffer = new MemoryStream();
        fontStream.CopyTo(fontBuffer);
        var fontBytes = fontBuffer.ToArray();

        using var source = SKBitmap.Decode(Convert.FromBase64String(SourcePngBase64));
        Assert.NotNull(source);
        Assert.Equal(16, source.Width);
        Assert.Equal(16, source.Height);

        const int outputWidth = 240;
        const int outputHeight = 360;
        using var surface = new SKBitmap(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(surface))
        {
            canvas.DrawBitmap(source, new SKRect(0, 0, outputWidth, outputHeight));

            using var pillPaint = new SKPaint { Color = new SKColor(0x11, 0x18, 0x27), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(12, outputHeight - 44, outputWidth - 12, outputHeight - 12), 8, 8, pillPaint);

            using var typeface = SKTypeface.FromData(SKData.CreateCopy(fontBytes));
            Assert.NotNull(typeface);

            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var font = new SKFont(typeface, 18f);
            canvas.DrawText("1080p Blu-ray", 20, outputHeight - 24, font, textPaint);
        }

        using var outputImage = SKImage.FromBitmap(surface);
        using var outputData = outputImage.Encode(SKEncodedImageFormat.Png, 100);
        Assert.NotNull(outputData);
        var outputBytes = outputData.ToArray();
        Assert.NotEmpty(outputBytes);

        // PNG signature.
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, outputBytes[..8]);

        // IHDR: 8 bits per channel, non-interlaced, RGB (2) or RGBA (6).
        Assert.Equal(13, ReadBigEndianInt32(outputBytes, 8));
        Assert.Equal("IHDR", Encoding.ASCII.GetString(outputBytes, 12, 4));
        Assert.Equal(outputWidth, ReadBigEndianInt32(outputBytes, 16));
        Assert.Equal(outputHeight, ReadBigEndianInt32(outputBytes, 20));
        Assert.Equal(8, outputBytes[24]);
        Assert.Contains(outputBytes[25], new byte[] { 2, 6 });
        Assert.Equal(0, outputBytes[26]);
        Assert.Equal(0, outputBytes[27]);
        Assert.Equal(0, outputBytes[28]);

        // Decode-back verification of the encoded result.
        using var verify = SKBitmap.Decode(outputBytes);
        Assert.NotNull(verify);
        Assert.Equal(outputWidth, verify.Width);
        Assert.Equal(outputHeight, verify.Height);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}

/// <summary>
/// A fact that runs only when the pinned SkiaSharp native runtime is reachable
/// and explicitly enabled. The round trip needs the transitive
/// <c>libfontconfig.so.1</c> on the dynamic loader path; the default suite does
/// not set it, so this fact is reported as skipped unless
/// <c>ARRTAGS_SKIA_COMPAT=1</c> is set. The exact enabling command is recorded
/// in <c>docs/research/skia-host-compatibility.md</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkiaNativeFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaNativeFactAttribute"/> class.
    /// </summary>
    public SkiaNativeFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ARRTAGS_SKIA_COMPAT"), "1", StringComparison.Ordinal))
        {
            Skip = "Set ARRTAGS_SKIA_COMPAT=1 with the pinned SkiaSharp native runtime on the loader path to run the compatibility round trip.";
        }
    }
}
