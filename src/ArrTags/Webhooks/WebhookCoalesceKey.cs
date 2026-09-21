using System;
using ArrTags.Providers;

namespace ArrTags.Webhooks;

/// <summary>
/// The bounded in-memory coalescing identity of one inbound webhook delivery:
/// the provider family, the mapped event kind, and the advertised provider
/// record/file identifiers. It is used only to collapse duplicate, out-of-order,
/// and replayed deliveries within a short bounded window before any resolution
/// work is attempted. It never contains a secret, header, or raw payload.
/// </summary>
public readonly struct WebhookCoalesceKey : IEquatable<WebhookCoalesceKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookCoalesceKey"/> struct.
    /// </summary>
    /// <param name="providerKind">The provider family.</param>
    /// <param name="eventType">The mapped event kind.</param>
    /// <param name="seriesId">The advertised series identifier, if any.</param>
    /// <param name="episodeId">The advertised episode identifier, if any.</param>
    /// <param name="movieId">The advertised movie identifier, if any.</param>
    /// <param name="fileId">The advertised file identifier, if any.</param>
    public WebhookCoalesceKey(
        ArrProviderKind providerKind,
        WebhookEventType eventType,
        int? seriesId = null,
        int? episodeId = null,
        int? movieId = null,
        int? fileId = null)
    {
        ProviderKind = providerKind;
        EventType = eventType;
        SeriesId = seriesId;
        EpisodeId = episodeId;
        MovieId = movieId;
        FileId = fileId;
    }

    /// <summary>Gets the provider family.</summary>
    public ArrProviderKind ProviderKind { get; }

    /// <summary>Gets the mapped event kind.</summary>
    public WebhookEventType EventType { get; }

    /// <summary>Gets the advertised series identifier, if any.</summary>
    public int? SeriesId { get; }

    /// <summary>Gets the advertised episode identifier, if any.</summary>
    public int? EpisodeId { get; }

    /// <summary>Gets the advertised movie identifier, if any.</summary>
    public int? MovieId { get; }

    /// <summary>Gets the advertised file identifier, if any.</summary>
    public int? FileId { get; }

    /// <summary>
    /// Determines whether two keys are equal.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns><see langword="true"/> when every field is equal.</returns>
    public static bool operator ==(WebhookCoalesceKey left, WebhookCoalesceKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two keys differ.
    /// </summary>
    /// <param name="left">The left key.</param>
    /// <param name="right">The right key.</param>
    /// <returns><see langword="true"/> when any field differs.</returns>
    public static bool operator !=(WebhookCoalesceKey left, WebhookCoalesceKey right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(WebhookCoalesceKey other)
    {
        return ProviderKind == other.ProviderKind
            && EventType == other.EventType
            && SeriesId == other.SeriesId
            && EpisodeId == other.EpisodeId
            && MovieId == other.MovieId
            && FileId == other.FileId;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is WebhookCoalesceKey other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ProviderKind, EventType, SeriesId, EpisodeId, MovieId, FileId);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormattableString.Invariant(
            $"WebhookCoalesceKey({ProviderKind}, {EventType}, s={SeriesId}, e={EpisodeId}, m={MovieId}, f={FileId})");
    }
}
