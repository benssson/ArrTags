using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// Persisted configuration for one V1 badge selector. An entry overrides the
/// code-owned ADR-009 default for its selector: whether the selector
/// participates in rendering and its bounded provider-neutral display template.
/// It never references a Sonarr or Radarr DTO path, record identifier, quality
/// profile, credential, or extension value.
/// </summary>
public sealed class BadgeSelectorConfiguration
{
    /// <summary>
    /// Gets or sets the provider-neutral V1 selector this entry configures.
    /// </summary>
    public BadgeSelector Selector { get; set; } = BadgeSelector.Quality;

    /// <summary>
    /// Gets or sets a value indicating whether the selector participates in
    /// rendering. A disabled selector produces no badge even when its canonical
    /// value is confirmed.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the bounded display template. It contains at most one
    /// <c>{value}</c> placeholder and is subject to the same text and layout
    /// limits as the code-owned defaults. A template without a placeholder
    /// renders its literal text.
    /// </summary>
    public string Template { get; set; } = BadgeDefinition.ValuePlaceholder;
}
