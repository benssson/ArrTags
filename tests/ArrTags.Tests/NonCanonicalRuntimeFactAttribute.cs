using System;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// A fact that runs only when an explicitly produced non-canonical-runtime golden
/// set is supplied. It has its own environment variable because it is a separate
/// cross-runtime validation input, not a general native-runtime guard: the
/// canonical suite is green without it, and when it is enabled the expected
/// <c>Goldens/non-canonical</c> set must be present or the test fails rather than
/// silently passing. No non-canonical runtime exists in the current environment,
/// so this fact is reported as skipped by default.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NonCanonicalRuntimeFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NonCanonicalRuntimeFactAttribute"/> class.
    /// </summary>
    public NonCanonicalRuntimeFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ARRTAGS_NONCANONICAL_GOLDENS"), "1", StringComparison.Ordinal))
        {
            Skip = "Set ARRTAGS_NONCANONICAL_GOLDENS=1 with a Goldens/non-canonical set produced on the selected non-canonical Linux runtime to run the tolerant cross-runtime comparison.";
        }
    }
}
