using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Rendering;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// The separate-process half of the task 4.11 byte-determinism contract. This
/// test only acts when the parent determinism test provides
/// <c>ARRTAGS_DETERMINISM_PROBE_OUT</c>; during a normal run it is a no-op. When
/// it acts, it renders the canonical fixture in a fresh process and writes the
/// exact PNG bytes for the parent to compare. It never writes a golden.
/// </summary>
public class RenderDeterminismProcessProbe
{
    [SkiaNativeFact]
    public async Task RenderCanonicalFixtureAndWriteArtifact()
    {
        var outputPath = Environment.GetEnvironmentVariable("ARRTAGS_DETERMINISM_PROBE_OUT");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        var renderer = new SkiaBadgeRenderer();
        var result = await renderer.RenderAsync(
            RenderGoldenFixtures.BuildRequest("all-fields"),
            CancellationToken.None);

        Assert.Equal(RenderStatus.Rendered, result.Status);
        await File.WriteAllBytesAsync(outputPath, result.PngBytes.ToArray());
    }
}
