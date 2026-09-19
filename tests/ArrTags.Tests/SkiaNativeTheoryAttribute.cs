using System;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// A theory that runs only when the pinned SkiaSharp native runtime is reachable
/// and explicitly enabled, matching <see cref="SkiaNativeFactAttribute"/>. The
/// golden and cross-runtime fixtures are parameterized, so they need the theory
/// form of the same honest environment guard. When the guard is unset the cases
/// are reported as skipped and the default <c>./build.sh test</c> stays green;
/// when <c>ARRTAGS_SKIA_COMPAT=1</c> is set without the pinned native runtime the
/// cases fail rather than silently passing.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkiaNativeTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaNativeTheoryAttribute"/> class.
    /// </summary>
    public SkiaNativeTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ARRTAGS_SKIA_COMPAT"), "1", StringComparison.Ordinal))
        {
            Skip = "Set ARRTAGS_SKIA_COMPAT=1 with the pinned SkiaSharp native runtime on the loader path to run the golden and cross-runtime fixtures.";
        }
    }
}
