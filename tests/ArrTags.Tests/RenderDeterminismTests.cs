using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Matching;
using ArrTags.Media;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// Phase 4 task 4.11 byte-determinism tests. The same request rendered
/// repeatedly, with a different item identity, with a different observation
/// timestamp, read through different stream chunk sizes, and in a separate fresh
/// process must produce identical decoded pixels, output hash, and PNG bytes.
/// The process-restart half is environment-guarded because it renders with the
/// native renderer.
/// </summary>
public class RenderDeterminismTests
{
    private static readonly IRenderer Renderer = new SkiaBadgeRenderer();

    [SkiaNativeFact]
    public async Task RepeatedRendersAreByteIdentical()
    {
        // The V1 default and a non-default anchor/size case must both be
        // byte-deterministic, so the committed anchor/size goldens are
        // reproducible.
        await AssertRepeatedRendersAreByteIdentical("all-fields");
        await AssertRepeatedRendersAreByteIdentical("top-right-large");
    }

    private static async Task AssertRepeatedRendersAreByteIdentical(string name)
    {
        var request = RenderGoldenFixtures.BuildRequest(name);
        var first = await Renderer.RenderAsync(request, CancellationToken.None);
        Assert.Equal(RenderStatus.Rendered, first.Status);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var next = await Renderer.RenderAsync(request, CancellationToken.None);
            Assert.Equal(first.PngBytes.ToArray(), next.PngBytes.ToArray());
            Assert.Equal(first.OutputHash, next.OutputHash);
            Assert.Equal(first.OutputFingerprint, next.OutputFingerprint);
            AssertPixelPlanesEqual(first.PngBytes.ToArray(), next.PngBytes.ToArray());
        }
    }

    [SkiaNativeFact]
    public async Task DifferentItemIdentityDoesNotChangeTheArtifact()
    {
        var requestA = RenderGoldenFixtures.BuildRequest("all-fields");
        var differentIdentity = new MediaIdentity(new Guid("77777777-7777-7777-7777-777777777777"), MediaItemType.Movie);
        var requestB = RenderTestFixtures.BuildRequest(
            requestA.SourceImage,
            identity: differentIdentity,
            match: RenderTestFixtures.BuildMatch(differentIdentity),
            metadata: requestA.Metadata);

        var first = await Renderer.RenderAsync(requestA, CancellationToken.None);
        var second = await Renderer.RenderAsync(requestB, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, first.Status);
        Assert.Equal(RenderStatus.Rendered, second.Status);
        Assert.Equal(first.PngBytes.ToArray(), second.PngBytes.ToArray());
        Assert.Equal(first.OutputHash, second.OutputHash);
        Assert.Equal(first.OutputFingerprint, second.OutputFingerprint);
    }

    [SkiaNativeFact]
    public async Task DifferentObservationTimestampDoesNotChangeTheArtifact()
    {
        var requestA = RenderGoldenFixtures.BuildRequest("all-fields");
        var requestB = RenderTestFixtures.BuildRequest(
            requestA.SourceImage,
            metadata: RenderGoldenFixtures.BuildAllFieldsMetadata(
                observedAt: new DateTimeOffset(2031, 12, 31, 23, 59, 58, TimeSpan.Zero)));

        var first = await Renderer.RenderAsync(requestA, CancellationToken.None);
        var second = await Renderer.RenderAsync(requestB, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, first.Status);
        Assert.Equal(RenderStatus.Rendered, second.Status);
        Assert.Equal(first.PngBytes.ToArray(), second.PngBytes.ToArray());
        Assert.Equal(first.OutputHash, second.OutputHash);
        Assert.Equal(first.OutputFingerprint, second.OutputFingerprint);
    }

    [SkiaNativeFact]
    public async Task DifferentStreamChunkingDoesNotChangeTheArtifact()
    {
        var source = RenderImageFixtures.CreateRgbPng(RenderGoldenFixtures.Width, RenderGoldenFixtures.Height);
        var bytes = source.Bytes.ToArray();

        var oneByteAtATime = ReadInChunks(bytes, 1);
        var largeChunks = ReadInChunks(bytes, 4096);

        var requestA = RenderTestFixtures.BuildRequest(
            RenderImageFixtures.Source(oneByteAtATime, RenderGoldenFixtures.Width, RenderGoldenFixtures.Height),
            metadata: RenderImageFixtures.BuildMetadata());
        var requestB = RenderTestFixtures.BuildRequest(
            RenderImageFixtures.Source(largeChunks, RenderGoldenFixtures.Width, RenderGoldenFixtures.Height),
            metadata: RenderImageFixtures.BuildMetadata());

        var first = await Renderer.RenderAsync(requestA, CancellationToken.None);
        var second = await Renderer.RenderAsync(requestB, CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, first.Status);
        Assert.Equal(RenderStatus.Rendered, second.Status);
        Assert.Equal(first.PngBytes.ToArray(), second.PngBytes.ToArray());
        Assert.Equal(first.OutputHash, second.OutputHash);
    }

    [SkiaNativeFact]
    public async Task SeparateProcessProducesTheSameArtifact()
    {
        var expected = await RenderAsync();
        var outputPath = Path.Combine(Path.GetTempPath(), $"arrtags-determinism-{Guid.NewGuid():N}.png");

        try
        {
            var exitCode = await RunProbeAsync(outputPath);
            Assert.True(exitCode == 0, $"The separate-process probe exited with code {exitCode}.");
            Assert.True(File.Exists(outputPath), "The separate-process probe did not produce an artifact.");

            var childBytes = await File.ReadAllBytesAsync(outputPath);
            Assert.Equal(expected.PngBytes.ToArray(), childBytes);
            Assert.Equal(expected.OutputHash, Convert.ToHexString(SHA256.HashData(childBytes)));
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static async Task<RenderResult> RenderAsync()
    {
        var result = await Renderer.RenderAsync(RenderGoldenFixtures.BuildRequest("all-fields"), CancellationToken.None);
        Assert.Equal(RenderStatus.Rendered, result.Status);
        return result;
    }

    private static byte[] ReadInChunks(byte[] bytes, int chunkSize)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var output = new MemoryStream();
        var buffer = new byte[chunkSize];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static void AssertPixelPlanesEqual(byte[] expected, byte[] actual)
    {
        var expectedPixels = RenderGoldenFixtures.DecodePixels(expected, out var width, out var height, out var channels);
        var actualPixels = RenderGoldenFixtures.DecodePixels(actual, out _, out _, out _);
        var comparison = CrossRuntimePixelComparator.Compare(
            expectedPixels,
            actualPixels,
            width,
            height,
            channels,
            PixelComparisonMode.Exact);
        Assert.True(comparison.IsMatch, comparison.Reason);
    }

    private static async Task<int> RunProbeAsync(string outputPath)
    {
        var repoRoot = FindRepoRoot();
        var testProject = Path.Combine(repoRoot, "tests", "ArrTags.Tests", "ArrTags.Tests.csproj");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(testProject);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add("FullyQualifiedName~RenderDeterminismProcessProbe");
        startInfo.Environment["ARRTAGS_DETERMINISM_PROBE_OUT"] = outputPath;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The dotnet host could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The separate-process render probe timed out.");
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"The separate-process probe failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");

        return process.ExitCode;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArrTags.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
