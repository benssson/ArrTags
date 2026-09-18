using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using ArrTags.Providers;

namespace ArrTags.Matching;

/// <summary>
/// The canonical, provider-neutral description of one Arr record that could be
/// matched to a Jellyfin item. It carries normalized external provider
/// identifiers, optional descriptive and numbering context, and the concrete
/// connection-scoped <see cref="ArrRecordIdentity"/>. It never contains a
/// provider DTO, credential, request URL, or provider-specific concept.
/// </summary>
public sealed class MatchCandidate
{
    private static readonly IReadOnlyDictionary<string, string> NoProviderIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchCandidate"/> class.
    /// </summary>
    /// <param name="connectionId">The connection scope for the contained local identifier.</param>
    /// <param name="providerKind">The provider family of the candidate record.</param>
    /// <param name="recordIdentity">The concrete, connection-scoped record and file identity.</param>
    /// <param name="providerIds">The normalized external provider identifiers when present.</param>
    /// <param name="title">The candidate title; candidate or diagnostic data only.</param>
    /// <param name="productionYear">The candidate production year; candidate or tie-breaker data only.</param>
    /// <param name="seasonNumber">The raw season number when applicable.</param>
    /// <param name="episodeNumber">The raw episode number when applicable.</param>
    /// <param name="episodeNumberEnd">The raw end of a multi-episode span when applicable.</param>
    /// <param name="primaryPath">The raw provider path when known, before any configured path normalization.</param>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The provider kind is undefined or a number is out of range.</exception>
    /// <exception cref="ArgumentException">The record identity does not belong to the supplied connection and provider scope.</exception>
    public MatchCandidate(
        ArrConnectionId connectionId,
        ArrProviderKind providerKind,
        ArrRecordIdentity recordIdentity,
        IReadOnlyDictionary<string, string>? providerIds = null,
        string? title = null,
        int? productionYear = null,
        int? seasonNumber = null,
        int? episodeNumber = null,
        int? episodeNumberEnd = null,
        string? primaryPath = null)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(recordIdentity);

        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind), providerKind, "Unknown Arr provider kind.");
        }

        if (!recordIdentity.ConnectionId.Equals(connectionId))
        {
            throw new ArgumentException(
                "A match candidate record identity must be scoped to the candidate connection.",
                nameof(recordIdentity));
        }

        if (recordIdentity.ProviderKind != providerKind)
        {
            throw new ArgumentException(
                "A match candidate record identity must belong to the candidate provider kind.",
                nameof(recordIdentity));
        }

        EnsureNonNegative(seasonNumber, nameof(seasonNumber));
        EnsureNonNegative(episodeNumber, nameof(episodeNumber));
        EnsureNonNegative(episodeNumberEnd, nameof(episodeNumberEnd));

        if (episodeNumber is int episode && episodeNumberEnd is int end && end < episode)
        {
            throw new ArgumentOutOfRangeException(
                nameof(episodeNumberEnd),
                episodeNumberEnd,
                "A multi-episode span cannot end before it starts.");
        }

        ConnectionId = connectionId;
        ProviderKind = providerKind;
        RecordIdentity = recordIdentity;
        ProviderIds = NormalizeProviderIds(providerIds);
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        ProductionYear = productionYear;
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
        EpisodeNumberEnd = episodeNumberEnd;
        PrimaryPath = string.IsNullOrWhiteSpace(primaryPath) ? null : primaryPath.Trim();
    }

    /// <summary>
    /// Gets the connection scope for the contained local identifier.
    /// </summary>
    public ArrConnectionId ConnectionId { get; }

    /// <summary>
    /// Gets the provider family of the candidate record.
    /// </summary>
    public ArrProviderKind ProviderKind { get; }

    /// <summary>
    /// Gets the concrete, connection-scoped record and file identity.
    /// </summary>
    public ArrRecordIdentity RecordIdentity { get; }

    /// <summary>
    /// Gets the normalized external provider identifiers when present.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; }

    /// <summary>
    /// Gets the candidate title. It is candidate or diagnostic data only and is
    /// never sufficient identity.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Gets the candidate production year. It is a candidate or tie-breaker
    /// only and is never sole proof of a match.
    /// </summary>
    public int? ProductionYear { get; }

    /// <summary>
    /// Gets the raw season number when applicable.
    /// </summary>
    public int? SeasonNumber { get; }

    /// <summary>
    /// Gets the raw episode number when applicable, before any numbering policy
    /// is applied.
    /// </summary>
    public int? EpisodeNumber { get; }

    /// <summary>
    /// Gets the raw end of a multi-episode span when applicable, before any
    /// numbering policy is applied.
    /// </summary>
    public int? EpisodeNumberEnd { get; }

    /// <summary>
    /// Gets the raw provider path when known, before any configured path
    /// normalization.
    /// </summary>
    public string? PrimaryPath { get; }

    private static void EnsureNonNegative(int? value, string parameterName)
    {
        if (value is int number && number < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A candidate number cannot be negative.");
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeProviderIds(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null || providerIds.Count == 0)
        {
            return NoProviderIds;
        }

        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in providerIds)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                copy[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        return copy.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
