using System;
using System.Collections.Generic;
using ArrTags.Providers;

namespace ArrTags.Webhooks;

/// <summary>
/// A bounded, provider-neutral, secret-free description of one authenticated
/// inbound Arr webhook. It carries only the provider family, the bounded event
/// kind, and the provider-local record/file identifiers that the payload
/// advertised. Those identifiers are hints only: they are never trusted as a
/// source of truth, never grant permission to perform work for an arbitrary
/// item, and are only used to find Jellyfin items that ArrTags already
/// associated with the provider record (ADR-012). The event never carries the
/// shared secret, a credential lease, a header value, a raw payload, or any
/// other unbounded external data, so it is safe to retain and report.
/// </summary>
public sealed class WebhookEvent : IEquatable<WebhookEvent>
{
    private static readonly IReadOnlyList<int> NoEpisodeIds = Array.Empty<int>();

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookEvent"/> class.
    /// </summary>
    /// <param name="providerKind">The provider family the receiving route identified.</param>
    /// <param name="eventType">The bounded mapped event kind.</param>
    /// <param name="isUpgrade">Whether the provider flagged an upgrade.</param>
    /// <param name="seriesId">The Sonarr-local series identifier when advertised.</param>
    /// <param name="episodeIds">The bounded Sonarr-local episode identifiers when advertised.</param>
    /// <param name="episodeFileId">The Sonarr-local episode-file identifier when advertised.</param>
    /// <param name="movieId">The Radarr-local movie identifier when advertised.</param>
    /// <param name="movieFileId">The Radarr-local movie-file identifier when advertised.</param>
    public WebhookEvent(
        ArrProviderKind providerKind,
        WebhookEventType eventType,
        bool isUpgrade = false,
        int? seriesId = null,
        IReadOnlyList<int>? episodeIds = null,
        int? episodeFileId = null,
        int? movieId = null,
        int? movieFileId = null)
    {
        ProviderKind = providerKind;
        EventType = eventType;
        IsUpgrade = isUpgrade;
        SeriesId = seriesId;
        EpisodeIds = episodeIds ?? NoEpisodeIds;
        EpisodeFileId = episodeFileId;
        MovieId = movieId;
        MovieFileId = movieFileId;
    }

    /// <summary>
    /// Gets the provider family the receiving route identified.
    /// </summary>
    public ArrProviderKind ProviderKind { get; }

    /// <summary>
    /// Gets the bounded mapped event kind.
    /// </summary>
    public WebhookEventType EventType { get; }

    /// <summary>
    /// Gets a value indicating whether the provider flagged an upgrade.
    /// </summary>
    public bool IsUpgrade { get; }

    /// <summary>
    /// Gets the advertised Sonarr-local series identifier, if any.
    /// </summary>
    public int? SeriesId { get; }

    /// <summary>
    /// Gets the bounded advertised Sonarr-local episode identifiers.
    /// </summary>
    public IReadOnlyList<int> EpisodeIds { get; }

    /// <summary>
    /// Gets the advertised Sonarr-local episode-file identifier, if any.
    /// </summary>
    public int? EpisodeFileId { get; }

    /// <summary>
    /// Gets the advertised Radarr-local movie identifier, if any.
    /// </summary>
    public int? MovieId { get; }

    /// <summary>
    /// Gets the advertised Radarr-local movie-file identifier, if any.
    /// </summary>
    public int? MovieFileId { get; }

    /// <summary>
    /// Gets a value indicating whether this event kind can produce a bounded
    /// reconciliation hint. <see cref="WebhookEventType.Unsupported"/> events are
    /// acknowledged but never produce work.
    /// </summary>
    public bool ProducesReconciliationHint => EventType is not WebhookEventType.Unsupported;

    /// <summary>
    /// Gets the bounded coalescing key for duplicate, out-of-order, and replayed
    /// deliveries of the same provider record/file event.
    /// </summary>
    public WebhookCoalesceKey CoalesceKey => new(
        ProviderKind,
        EventType,
        SeriesId,
        EpisodeIds.Count > 0 ? EpisodeIds[0] : null,
        MovieId,
        EpisodeFileId ?? MovieFileId);

    /// <inheritdoc />
    public bool Equals(WebhookEvent? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ProviderKind != other.ProviderKind
            || EventType != other.EventType
            || IsUpgrade != other.IsUpgrade
            || SeriesId != other.SeriesId
            || EpisodeFileId != other.EpisodeFileId
            || MovieId != other.MovieId
            || MovieFileId != other.MovieFileId
            || EpisodeIds.Count != other.EpisodeIds.Count)
        {
            return false;
        }

        for (var index = 0; index < EpisodeIds.Count; index++)
        {
            if (EpisodeIds[index] != other.EpisodeIds[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as WebhookEvent);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(ProviderKind);
        hash.Add(EventType);
        hash.Add(IsUpgrade);
        hash.Add(SeriesId);
        hash.Add(EpisodeFileId);
        hash.Add(MovieId);
        hash.Add(MovieFileId);
        foreach (var episodeId in EpisodeIds)
        {
            hash.Add(episodeId);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormattableString.Invariant(
            $"WebhookEvent({ProviderKind}, {EventType}, series={SeriesId}, episodes={EpisodeIds.Count}, movie={MovieId})");
    }
}
