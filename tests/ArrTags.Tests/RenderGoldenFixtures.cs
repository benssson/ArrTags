using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ArrTags.Metadata;
using ArrTags.Rendering;
using SkiaSharp;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 golden fixtures and store. The case list, expected
/// dimensions, channel count, and PNG color type are pure data so test discovery
/// never touches the native renderer. Sources, metadata, and the decoded golden
/// pixel planes are produced only inside environment-guarded tests. The goldens
/// are read-only committed artifacts; there is no writer or auto-approval path.
/// </summary>
internal static class RenderGoldenFixtures
{
    /// <summary>The default fixture width.</summary>
    public const int Width = 500;

    /// <summary>The default fixture height.</summary>
    public const int Height = 750;

    /// <summary>The oriented width of the EXIF orientation-6 golden.</summary>
    public const int OrientedWidth = 750;

    /// <summary>The oriented height of the EXIF orientation-6 golden.</summary>
    public const int OrientedHeight = 500;

    /// <summary>The manifest file name under the committed goldens directory.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>The deterministic golden fixture names in ADR-010 order.</summary>
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "opaque-jpeg",
        "rgb-png",
        "rgba-png",
        "orientation",
        "all-fields",
        "long-custom",
        "missing-fields",
        "full-rail",
        "upgrade-status",
    };

    /// <summary>
    /// Returns the independent expected output dimensions, channel count, and PNG
    /// color type for the named fixture.
    /// </summary>
    public static (int Width, int Height, int Channels, int PngColorType) Expected(string name)
    {
        if (name == "orientation")
        {
            return (OrientedWidth, OrientedHeight, 3, 2);
        }

        if (name == "rgba-png")
        {
            return (Width, Height, 4, 6);
        }

        if (!Names.Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown golden fixture.");
        }

        return (Width, Height, 3, 2);
    }

    /// <summary>
    /// Builds the render request for the named fixture. This creates the synthetic
    /// Skia source and must only run inside an environment-guarded test.
    /// </summary>
    public static RenderRequest BuildRequest(string name)
    {
        return name switch
        {
            "opaque-jpeg" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateOpaqueJpeg(Width, Height),
                metadata: TechnicalMetadata()),
            "rgb-png" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateRgbPng(Width, Height),
                metadata: TechnicalMetadata()),
            "rgba-png" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateRgbaPng(Width, Height),
                metadata: TechnicalMetadata()),
            "orientation" => BuildOrientationRequest(),
            "all-fields" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateRgbPng(Width, Height),
                metadata: BuildAllFieldsMetadata()),
            "long-custom" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateRgbPng(Width, Height),
                metadata: RenderImageFixtures.BuildMetadata(
                    customBadges: new[]
                    {
                        "ThisIsAVeryLongCustomBadgeValueThatExceedsTheLimit",
                        "Short",
                    })),
            "missing-fields" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateRgbPng(Width, Height),
                metadata: RenderImageFixtures.BuildMetadata(
                    quality: null,
                    resolution: null,
                    videoCodec: null,
                    source: "WEB-DL")),
            "full-rail" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateRgbPng(Width, Height),
                metadata: FullRailMetadata()),
            "upgrade-status" => RenderTestFixtures.BuildRequest(
                RenderImageFixtures.CreateRgbPng(Width, Height),
                metadata: RenderImageFixtures.BuildMetadata(upgradePending: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown golden fixture."),
        };
    }

    /// <summary>
    /// Decodes a rendered PNG into a packed pixel plane in RGB or RGBA channel
    /// order. The decoded plane is the golden comparison unit, not the encoded
    /// bytes alone.
    /// </summary>
    public static byte[] DecodePixels(byte[] png, out int width, out int height, out int channels)
    {
        using var bitmap = SKBitmap.Decode(png);
        if (bitmap is null)
        {
            throw new InvalidOperationException("The PNG could not be decoded.");
        }

        width = bitmap.Width;
        height = bitmap.Height;
        channels = RenderImageFixtures.ReadPngColorType(png) == 6 ? 4 : 3;

        var pixels = new byte[width * height * channels];
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                pixels[index++] = color.Red;
                pixels[index++] = color.Green;
                pixels[index++] = color.Blue;
                if (channels == 4)
                {
                    pixels[index++] = color.Alpha;
                }
            }
        }

        return pixels;
    }

    private static RenderRequest BuildOrientationRequest()
    {
        // Task 4.11 uses the dimension-swapping EXIF orientation 6 (rotate 90
        // clockwise) with an asymmetric corner marker so the golden encodes the
        // corrected axis-swapping transform as a decoded pixel plane. The
        // remaining orientations are covered by RenderOrientationTests.
        var bytes = RenderImageFixtures.CreateMarkedOrientedJpeg(Width, Height, orientation: 6);
        var source = RenderImageFixtures.JpegSource(bytes, OrientedWidth, OrientedHeight);
        return RenderTestFixtures.BuildRequest(source, metadata: TechnicalMetadata());
    }

    private static BadgeMetadata TechnicalMetadata()
    {
        return RenderImageFixtures.BuildMetadata(
            audioCodec: "EAC3",
            audioChannels: 6);
    }

    /// <summary>
    /// Builds the every-V1-field metadata used by the all-fields fixture. The
    /// observation timestamp is overridable so determinism tests can prove it is
    /// not an output-affecting input.
    /// </summary>
    public static BadgeMetadata BuildAllFieldsMetadata(DateTimeOffset? observedAt = null)
    {
        return RenderImageFixtures.BuildMetadata(
            quality: "Remux-2160p",
            resolution: "2160p",
            dynamicRangeKind: ArrDynamicRangeKind.Hdr10,
            dolbyVision: true,
            source: "Blu-ray",
            videoCodec: "hevc",
            audioCodec: "TrueHD",
            audioChannels: 8,
            audioFeatures: new[] { ArrAudioFeature.Atmos, ArrAudioFeature.DtsHd },
            upgradePending: true,
            customBadges: new[] { "HDR10+", "Atmos" },
            observedAt: observedAt);
    }

    private static BadgeMetadata FullRailMetadata()
    {
        return RenderImageFixtures.BuildMetadata(
            dynamicRangeKind: ArrDynamicRangeKind.Hdr10Plus,
            audioCodec: "DTS-HD",
            audioChannels: 6,
            audioFeatures: new[] { ArrAudioFeature.DtsHd },
            customBadges: new[] { "Director", "Extended", "Remastered", "Limited" });
    }
}

/// <summary>
/// The committed manifest entry for one golden fixture.
/// </summary>
internal sealed class GoldenManifestEntry
{
    /// <summary>Gets or sets the fixture name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the committed PNG file name.</summary>
    public string PngFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected output width.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the expected output height.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the expected decoded channel count.</summary>
    public int Channels { get; set; }

    /// <summary>Gets or sets the expected PNG color type.</summary>
    public int PngColorType { get; set; }

    /// <summary>Gets or sets the expected uppercase SHA-256 of the encoded PNG.</summary>
    public string OutputHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected deterministic output fingerprint.</summary>
    public string OutputFingerprint { get; set; } = string.Empty;
}

/// <summary>
/// The loaded golden artifact: the committed bytes plus the declared identity.
/// </summary>
internal sealed class GoldenArtifact
{
    /// <summary>Gets the committed encoded PNG bytes.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>Gets the manifest entry for the artifact.</summary>
    public required GoldenManifestEntry Entry { get; init; }
}

/// <summary>
/// Loads the read-only committed golden artifacts from the test output directory.
/// </summary>
internal static class GoldenStore
{
    /// <summary>Gets the directory containing the committed golden artifacts.</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Goldens");

    /// <summary>
    /// Loads the manifest and the named golden artifact.
    /// </summary>
    public static GoldenArtifact Load(string name)
    {
        var entries = GoldenManifestReader.ReadAll();
        var entry = entries.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        var bytes = File.ReadAllBytes(Path.Combine(Directory, entry.PngFile));
        return new GoldenArtifact { Bytes = bytes, Entry = entry };
    }
}

/// <summary>
/// Reads the read-only committed golden manifest.
/// </summary>
internal static class GoldenManifestReader
{
    /// <summary>
    /// Reads every manifest entry from the default golden directory.
    /// </summary>
    public static IReadOnlyList<GoldenManifestEntry> ReadAll()
    {
        return ReadAll(GoldenStore.Directory);
    }

    /// <summary>
    /// Reads every manifest entry from the supplied golden directory.
    /// </summary>
    public static IReadOnlyList<GoldenManifestEntry> ReadAll(string directory)
    {
        var path = Path.Combine(directory, RenderGoldenFixtures.ManifestFileName);
        return JsonSerializer.Deserialize<List<GoldenManifestEntry>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("The golden manifest was empty.");
    }
}

/// <summary>
/// The data-driven entry point for an optional, separately produced
/// non-canonical-runtime golden set. It lives beside the canonical goldens under
/// <c>Goldens/non-canonical/</c> with the same manifest shape. When it is absent
/// (the current environment has only the canonical runtime) the tolerant
/// cross-runtime test reports an explicit environment limitation instead of
/// fabricating a result; when it is supplied, the same comparator runs against it
/// with no code change.
/// </summary>
internal static class NonCanonicalGoldenStore
{
    /// <summary>Gets the optional non-canonical golden directory.</summary>
    public static string Directory => Path.Combine(GoldenStore.Directory, "non-canonical");

    /// <summary>Gets a value indicating whether a non-canonical golden set is present.</summary>
    public static bool Exists =>
        File.Exists(Path.Combine(Directory, RenderGoldenFixtures.ManifestFileName));

    /// <summary>
    /// Reads the non-canonical manifest entries.
    /// </summary>
    public static IReadOnlyList<GoldenManifestEntry> ReadAll() => GoldenManifestReader.ReadAll(Directory);

    /// <summary>
    /// Loads the named non-canonical golden artifact.
    /// </summary>
    public static GoldenArtifact Load(string name)
    {
        var entry = ReadAll().Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        var bytes = File.ReadAllBytes(Path.Combine(Directory, entry.PngFile));
        return new GoldenArtifact { Bytes = bytes, Entry = entry };
    }
}
