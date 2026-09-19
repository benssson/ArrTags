using System;
using Xunit;

namespace ArrTags.Tests;

/// <summary>
/// A fact that runs only when the pinned Jellyfin 12.0.0 host install directory
/// is supplied through <c>ARRTAGS_JELLYFIN_HOST_DIR</c>. The task 5.1 route and
/// authorization confirmations must reflect over the pinned host's
/// <c>Jellyfin.Api.dll</c>, which is not a NuGet package and therefore is not
/// present in the test output. When the variable is unset the fact is reported
/// as skipped so the default <c>./build.sh test</c> run stays green; when it is
/// set but the pinned assembly is missing or cannot be loaded, the test fails
/// rather than passing silently.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class JellyfinHostFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinHostFactAttribute"/> class.
    /// </summary>
    public JellyfinHostFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARRTAGS_JELLYFIN_HOST_DIR")))
        {
            Skip = "Set ARRTAGS_JELLYFIN_HOST_DIR to the pinned Jellyfin 12.0.0 host install directory to reflect over Jellyfin.Api.dll.";
        }
    }
}
