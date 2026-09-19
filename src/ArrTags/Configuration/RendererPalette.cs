using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// Resolves configured palette overrides against the code-owned ADR-009
/// defaults. An empty or malformed override falls back to the code-owned default,
/// and a valid override is emitted in canonical <c>#RRGGBB</c> form so a
/// semantically equal color always produces the same effective policy and
/// renderer configuration fingerprint.
/// </summary>
internal static class RendererPalette
{
    /// <summary>
    /// Resolves one configured color to its canonical effective value.
    /// </summary>
    /// <param name="configured">The configured override, or <see langword="null"/> or empty when unset.</param>
    /// <param name="codeOwnedDefault">The ADR-009 default for the color.</param>
    /// <returns>The canonical <c>#RRGGBB</c> effective color.</returns>
    public static string Resolve(string? configured, string codeOwnedDefault)
    {
        if (!string.IsNullOrWhiteSpace(configured) && RgbColor.TryParse(configured, out var configuredColor))
        {
            return configuredColor.ToString();
        }

        return RgbColor.TryParse(codeOwnedDefault, out var defaultColor)
            ? defaultColor.ToString()
            : codeOwnedDefault;
    }
}
