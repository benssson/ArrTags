using System;
using System.Collections.Generic;
using ArrTags.Configuration;
using ArrTags.Providers;
using ArrTags.Reconciliation;

namespace ArrTags.Webhooks;

/// <summary>
/// Resolves an authenticated, bounded <see cref="WebhookEvent"/> into the
/// Jellyfin items ArrTags has already associated with the advertised provider
/// record. It never trusts the payload as a source of truth and never treats a
/// provider record id as permission to work on an arbitrary item: it only
/// returns items that already exist in ArrTags' persisted metadata-state mapping
/// for the resolved connection. The lookup is bounded by the configured
/// reconciliation batch size, so a webhook can never drive unbounded work; an
/// unmatched event produces no hint and is repaired by the authoritative
/// periodic/library-event reconciliation (ADR-012).
/// </summary>
public sealed class WebhookReconciliationResolver
{
    private readonly MetadataStateStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookReconciliationResolver"/> class.
    /// </summary>
    /// <param name="store">The persisted metadata-state mapping.</param>
    /// <exception cref="ArgumentNullException">The store is <see langword="null"/>.</exception>
    public WebhookReconciliationResolver(MetadataStateStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Resolves the bounded set of Jellyfin item ids already associated with the
    /// advertised provider record on the resolved connection.
    /// </summary>
    /// <param name="webhookEvent">The authenticated, bounded webhook event.</param>
    /// <param name="snapshot">The current immutable configuration snapshot.</param>
    /// <returns>The bounded, distinct Jellyfin item ids; empty when nothing matches.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public IReadOnlyList<Guid> Resolve(WebhookEvent webhookEvent, PluginConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!webhookEvent.ProducesReconciliationHint)
        {
            return Array.Empty<Guid>();
        }

        var connection = ResolveConnection(snapshot, webhookEvent.ProviderKind);
        if (connection is null || !connection.Enabled)
        {
            // A disabled or unavailable connection cannot produce badge work.
            return Array.Empty<Guid>();
        }

        var scanLimit = snapshot.Limits.ReconciliationBatchSize;
        if (scanLimit < 1)
        {
            scanLimit = 1;
        }

        var entries = _store.Enumerate(scanLimit);
        if (entries.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var matches = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (matches.Count >= scanLimit)
            {
                break;
            }

            if (entry.ProviderKind != webhookEvent.ProviderKind
                || !string.Equals(entry.ConnectionId, connection.ConnectionId.Value, StringComparison.Ordinal)
                || !Matches(entry.RecordIdentity, webhookEvent))
            {
                continue;
            }

            if (seen.Add(entry.JellyfinItemId))
            {
                matches.Add(entry.JellyfinItemId);
            }
        }

        return matches;
    }

    private static bool Matches(MetadataRecordIdentity? identity, WebhookEvent webhookEvent)
    {
        if (identity is null || identity.ProviderKind != webhookEvent.ProviderKind)
        {
            return false;
        }

        switch (webhookEvent.ProviderKind)
        {
            case ArrProviderKind.Radarr:
                return webhookEvent.MovieId is int movieId
                    && identity.MovieId == movieId;

            case ArrProviderKind.Sonarr:
                if (webhookEvent.SeriesId is not int seriesId || identity.SeriesId != seriesId)
                {
                    return false;
                }

                if (webhookEvent.EpisodeIds.Count == 0)
                {
                    // A series-level event reconciles every known episode of the
                    // series.
                    return true;
                }

                return identity.EpisodeId is int episodeId && Contains(webhookEvent.EpisodeIds, episodeId);

            default:
                return false;
        }
    }

    private static bool Contains(IReadOnlyList<int> values, int value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static ArrConnection? ResolveConnection(PluginConfigurationSnapshot snapshot, ArrProviderKind kind)
    {
        foreach (var connection in ArrConnectionCatalog.FromSnapshot(snapshot))
        {
            if (connection.Provider.Kind == kind)
            {
                return connection;
            }
        }

        return null;
    }
}
