using System.Collections.ObjectModel;
using ArrTags.Rendering;

namespace ArrTags.Configuration;

/// <summary>
/// Persisted configuration for one V1 badge selector. An entry overrides the
/// code-owned ADR-009 default for its selector: whether the selector
/// participates in rendering, its bounded provider-neutral display template,
/// and an optional bounded value allowlist. It never references a Sonarr or
/// Radarr DTO path, record identifier, quality profile, credential, or
/// extension value.
/// </summary>
public sealed class BadgeSelectorConfiguration
{
    private Collection<string> _allowedValues = new Collection<string>();

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

    /// <summary>
    /// Gets or sets the bounded provider-neutral value allowlist for this
    /// selector (ADR-017). An empty list means no restriction: every confirmed
    /// value passes. Each entry is compared to the resolved pre-template value
    /// with a case-insensitive ordinal exact match. Entries are trimmed and must
    /// be unique after case-insensitive comparison. The value never references a
    /// provider DTO path, record identifier, quality profile, credential, or
    /// extension value.
    /// </summary>
    /// <remarks>
    /// The property is settable so the elevation-gated <c>PluginsController</c>
    /// POST round-trip can populate it, matching the
    /// <see cref="RendererConfiguration.Selectors"/> shape (ADR-016 clause 7).
    /// </remarks>
#pragma warning disable CA2227 // The configuration is a replacement-snapshot DTO; the setter is required for the POST round-trip.
    public Collection<string> AllowedValues
    {
        get => _allowedValues;
        set => _allowedValues = value ?? new Collection<string>();
    }
#pragma warning restore CA2227
}
