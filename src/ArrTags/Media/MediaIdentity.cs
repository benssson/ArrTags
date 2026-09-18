using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace ArrTags.Media;

/// <summary>
/// The canonical, immutable identity and minimal media context for one Jellyfin
/// item. It is used for matching and cache scoping, not as a copy of the
/// complete Jellyfin entity, and it never contains provider DTOs, credentials,
/// or provider-specific facts.
/// </summary>
public sealed class MediaIdentity
{
    private static readonly IReadOnlyDictionary<string, string> NoProviderIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaIdentity"/> class.
    /// </summary>
    /// <param name="jellyfinItemId">The non-empty Jellyfin item identifier used as the local subject key.</param>
    /// <param name="itemType">The supported structural item type.</param>
    /// <param name="libraryId">The owning collection-folder identifier when known.</param>
    /// <param name="providerIds">The normalized external provider identifiers when present.</param>
    /// <param name="title">The display title; candidate or diagnostic data only.</param>
    /// <param name="productionYear">The production year; candidate or diagnostic data only.</param>
    /// <param name="seriesIdentity">The parent series identity for a season or episode when known.</param>
    /// <param name="seasonNumber">The raw season number when applicable.</param>
    /// <param name="episodeNumber">The raw episode number when applicable.</param>
    /// <param name="episodeNumberEnd">The raw end of a multi-episode span when applicable.</param>
    /// <param name="mediaLocation">The raw location and media-source summary when known.</param>
    /// <param name="sourceFingerprint">The opaque fingerprint of the Jellyfin source identity when known.</param>
    /// <exception cref="ArgumentException">The Jellyfin item identifier is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The item type is not defined.</exception>
    public MediaIdentity(
        Guid jellyfinItemId,
        MediaItemType itemType,
        Guid? libraryId = null,
        IReadOnlyDictionary<string, string>? providerIds = null,
        string? title = null,
        int? productionYear = null,
        MediaIdentity? seriesIdentity = null,
        int? seasonNumber = null,
        int? episodeNumber = null,
        int? episodeNumberEnd = null,
        MediaLocationSummary? mediaLocation = null,
        string? sourceFingerprint = null)
    {
        if (jellyfinItemId == Guid.Empty)
        {
            throw new ArgumentException("A media identity requires a non-empty Jellyfin item identifier.", nameof(jellyfinItemId));
        }

        if (!Enum.IsDefined(itemType))
        {
            throw new ArgumentOutOfRangeException(nameof(itemType), itemType, "Unknown media item type.");
        }

        JellyfinItemId = jellyfinItemId;
        ItemType = itemType;
        LibraryId = libraryId;
        ProviderIds = providerIds is null
            ? NoProviderIds
            : providerIds.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        Title = title;
        ProductionYear = productionYear;
        SeriesIdentity = seriesIdentity;
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
        EpisodeNumberEnd = episodeNumberEnd;
        MediaLocation = mediaLocation;
        SourceFingerprint = sourceFingerprint;
    }

    /// <summary>
    /// Gets the Jellyfin item identifier. It is stable only within the Jellyfin
    /// server and is used as the local subject key.
    /// </summary>
    public Guid JellyfinItemId { get; }

    /// <summary>
    /// Gets the supported structural item type.
    /// </summary>
    public MediaItemType ItemType { get; }

    /// <summary>
    /// Gets the owning collection-folder/library identifier when known. It is an
    /// identifier, never a display name, and is used for library eligibility.
    /// </summary>
    public Guid? LibraryId { get; }

    /// <summary>
    /// Gets the normalized external provider identifiers. Absence is valid and
    /// is never treated as identity proof on its own.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; }

    /// <summary>
    /// Gets the display title. It is candidate or diagnostic data only and is
    /// never sufficient identity.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Gets the production year. It is a candidate or tie-breaker only and is
    /// never sole proof of a match.
    /// </summary>
    public int? ProductionYear { get; }

    /// <summary>
    /// Gets the parent series identity for a season or episode when known.
    /// </summary>
    public MediaIdentity? SeriesIdentity { get; }

    /// <summary>
    /// Gets the raw season number for a season or episode when known. Season
    /// zero represents specials where applicable.
    /// </summary>
    public int? SeasonNumber { get; }

    /// <summary>
    /// Gets the raw episode number when known, before any numbering policy is applied.
    /// </summary>
    public int? EpisodeNumber { get; }

    /// <summary>
    /// Gets the raw end of a multi-episode span when known, before any numbering
    /// policy is applied.
    /// </summary>
    public int? EpisodeNumberEnd { get; }

    /// <summary>
    /// Gets the raw location and media-source summary when known.
    /// </summary>
    public MediaLocationSummary? MediaLocation { get; }

    /// <summary>
    /// Gets the opaque fingerprint of the Jellyfin source identity when known.
    /// It changes when the relevant source facts change and is distinct from the
    /// provider metadata fingerprint.
    /// </summary>
    public string? SourceFingerprint { get; }
}
