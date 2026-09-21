using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArrTags.Configuration;
using ArrTags.Media;
using ArrTags.Providers;
using ArrTags.Updates;

namespace ArrTags.Reconciliation;

/// <summary>
/// The Phase 6 reconciliation processor. It implements the work-item dispatch
/// boundary and owns the metadata-state publication path: re-read the current
/// Jellyfin item and current configuration, resolve the provider connection,
/// match the item, read and map the current provider metadata, compute the
/// canonical metadata fingerprint, re-validate that the work's basis is still
/// current, and only then atomically publish the metadata state.
/// </summary>
/// <remarks>
/// A long-running work item whose basis changed (configuration version advanced,
/// connection disabled or changed, item removed, changed, or no longer eligible)
/// is discarded rather than published. The processor publishes metadata state
/// only; it never invokes artwork generation or publication, which are owned by
/// later Phase 6 tasks.
/// </remarks>
public sealed class MetadataReconciliationProcessor : IWorkItemProcessor
{
    private readonly ConfigurationSnapshotService _configuration;
    private readonly IMediaLibraryResolver _library;
    private readonly IReadOnlyDictionary<ArrProviderKind, IArrMetadataReader> _readers;
    private readonly MetadataStateStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataReconciliationProcessor"/> class.
    /// </summary>
    /// <param name="configuration">The configuration snapshot service.</param>
    /// <param name="library">The Jellyfin library and item resolver boundary.</param>
    /// <param name="readers">The provider-neutral reconciliation readers, one per provider.</param>
    /// <param name="store">The metadata state store.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">More than one reader is registered for a provider.</exception>
    public MetadataReconciliationProcessor(
        ConfigurationSnapshotService configuration,
        IMediaLibraryResolver library,
        IEnumerable<IArrMetadataReader> readers,
        MetadataStateStore store)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(readers);

        var map = new Dictionary<ArrProviderKind, IArrMetadataReader>();
        foreach (var reader in readers)
        {
            if (reader is null)
            {
                continue;
            }

            if (!map.TryAdd(reader.Kind, reader))
            {
                throw new ArgumentException(
                    "More than one reconciliation reader is registered for a provider.",
                    nameof(readers));
            }
        }

        _readers = map;
    }

    /// <inheritdoc />
    public async Task<WorkProcessingResult> ProcessAsync(LibraryWorkItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _configuration.Current;
        if (item.ConfigurationVersion != snapshot.ConfigurationVersion)
        {
            return Discard(FormattableString.Invariant(
                $"The work was based on configuration v{item.ConfigurationVersion} but the current configuration is v{snapshot.ConfigurationVersion}."));
        }

        var currentItem = _library.ResolveItem(item.Key.ItemId);
        if (currentItem is null)
        {
            return Discard("The Jellyfin item is no longer present.");
        }

        if (!MediaIdentityFactory.TryCreate(currentItem, _library, out var identity) || identity is null)
        {
            return Discard("The Jellyfin item is no longer a supported media identity.");
        }

        if (!MediaEligibility.IsEligible(identity, snapshot))
        {
            return Discard("The Jellyfin item is no longer eligible for a badge.");
        }

        var kind = ResolveProviderKind(identity.ItemType);
        if (kind is null)
        {
            return Discard("The Jellyfin item type has no reconciliation provider.");
        }

        var connection = ResolveConnection(snapshot, kind.Value);
        if (connection is null || !connection.Enabled)
        {
            return Discard("The provider connection is not available.");
        }

        if (!_readers.TryGetValue(kind.Value, out var reader))
        {
            return WorkProcessingResult.Terminal("No reconciliation reader is registered for the provider.");
        }

        ArrMetadataReadResult read;
        try
        {
            read = await reader.ReadAsync(identity, connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An unclassified reader failure is terminal and never publishes.
            return WorkProcessingResult.Terminal("The provider read failed.");
        }

        if (!read.IsSuccess || read.Match is null)
        {
            return read.Error is null
                ? WorkProcessingResult.Terminal("The provider read returned no outcome.")
                : WorkProcessingResult.FromRetryability(read.Error.Retryability, read.Error.Message);
        }

        // Re-read the current item and configuration immediately before
        // publishing. A changed basis is discarded rather than published.
        var publishSnapshot = _configuration.Current;
        if (publishSnapshot.ConfigurationVersion != item.ConfigurationVersion
            || publishSnapshot.ConfigurationVersion != snapshot.ConfigurationVersion)
        {
            return Discard("The configuration changed while the work was processing.");
        }

        var publishItem = _library.ResolveItem(item.Key.ItemId);
        if (publishItem is null)
        {
            return Discard("The Jellyfin item was removed while the work was processing.");
        }

        if (!MediaIdentityFactory.TryCreate(publishItem, _library, out var publishIdentity) || publishIdentity is null)
        {
            return Discard("The Jellyfin item changed while the work was processing.");
        }

        if (!SubjectMatches(identity, publishIdentity))
        {
            return Discard("The Jellyfin item changed while the work was processing.");
        }

        if (!MediaEligibility.IsEligible(publishIdentity, publishSnapshot))
        {
            return Discard("The Jellyfin item is no longer eligible for a badge.");
        }

        var publishConnection = ResolveConnection(publishSnapshot, kind.Value);
        if (publishConnection is null
            || !publishConnection.Enabled
            || !publishConnection.ConnectionId.Equals(connection.ConnectionId))
        {
            return Discard("The provider connection changed while the work was processing.");
        }

        var entry = MetadataStateEntry.From(
            publishIdentity,
            read.Match,
            read.Metadata,
            DateTimeOffset.UtcNow,
            read.ProviderVersion,
            read.ProviderVersionToken);

        _store.Write(entry);

        return WorkProcessingResult.Completed(FormattableString.Invariant(
            $"Published metadata state '{entry.State}' for provider '{entry.ProviderKind.ToApiName()}'."));
    }

    private static ArrProviderKind? ResolveProviderKind(MediaItemType itemType)
    {
        return itemType switch
        {
            MediaItemType.Movie => ArrProviderKind.Radarr,
            MediaItemType.Episode => ArrProviderKind.Sonarr,
            _ => null,
        };
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

    private static bool SubjectMatches(MediaIdentity basis, MediaIdentity current)
    {
        return basis.ItemType == current.ItemType
            && basis.LibraryId == current.LibraryId
            && NormalizeNumber(basis.SeasonNumber) == NormalizeNumber(current.SeasonNumber)
            && NormalizeNumber(basis.EpisodeNumber) == NormalizeNumber(current.EpisodeNumber)
            && NormalizeNumber(basis.EpisodeNumberEnd) == NormalizeNumber(current.EpisodeNumberEnd)
            && basis.SeriesIdentity?.JellyfinItemId == current.SeriesIdentity?.JellyfinItemId
            && string.Equals(basis.SourceFingerprint, current.SourceFingerprint, StringComparison.Ordinal)
            && ProviderIdsMatch(basis, current);
    }

    private static string NormalizeNumber(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool ProviderIdsMatch(MediaIdentity basis, MediaIdentity current)
    {
        if (basis.ProviderIds.Count != current.ProviderIds.Count)
        {
            return false;
        }

        var expected = new StringBuilder();
        AppendProviderIds(expected, basis.ProviderIds);
        var actual = new StringBuilder();
        AppendProviderIds(actual, current.ProviderIds);
        return string.Equals(expected.ToString(), actual.ToString(), StringComparison.Ordinal);
    }

    private static void AppendProviderIds(StringBuilder builder, IReadOnlyDictionary<string, string> providerIds)
    {
        foreach (var entry in providerIds.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder
                .Append(entry.Key.ToUpperInvariant())
                .Append('=')
                .Append(entry.Value)
                .Append(';');
        }
    }

    private static WorkProcessingResult Discard(string reason)
    {
        return WorkProcessingResult.Completed(reason);
    }
}
