using System;
using System.Globalization;
using ArrTags.Providers;
using ArrTags.State;

namespace ArrTags.Reconciliation;

/// <summary>
/// Persists and reads the canonical <see cref="MetadataStateEntry"/> through the
/// versioned cache state boundary. Metadata state is last-known-good,
/// non-authoritative cache data: it is integrity-tagged and atomically written,
/// and a corrupt or semantically invalid entry is discarded and rebuilt from
/// provider state rather than quarantined or treated as authoritative.
/// </summary>
public sealed class MetadataStateStore
{
    /// <summary>
    /// The cache state record kind for metadata state.
    /// </summary>
    public const string RecordKind = "metadata-state";

    private readonly StateRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataStateStore"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository under the plugin data root.</param>
    /// <exception cref="ArgumentNullException">The repository is <see langword="null"/>.</exception>
    public MetadataStateStore(StateRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>
    /// Gets the stable, traversal-safe state record identifier for an item and
    /// provider. The record is replaced in place when a later observation
    /// changes the match, metadata, or fingerprint.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="providerKind">The provider family.</param>
    /// <returns>The record identifier.</returns>
    /// <exception cref="ArgumentException">The item identifier is empty or the provider is undefined.</exception>
    public static string GetRecordId(Guid jellyfinItemId, ArrProviderKind providerKind)
    {
        if (jellyfinItemId == Guid.Empty)
        {
            throw new ArgumentException("A metadata state record requires a non-empty Jellyfin item identifier.", nameof(jellyfinItemId));
        }

        return string.Concat(
            jellyfinItemId.ToString("N", CultureInfo.InvariantCulture),
            "-",
            providerKind.ToApiName());
    }

    /// <summary>
    /// Reads the metadata state for an item and provider. A corrupt or
    /// semantically invalid cache record is discarded and reported as
    /// rebuildable rather than returned as current state.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="providerKind">The provider family.</param>
    /// <returns>The state read outcome.</returns>
    public StateReadResult<MetadataStateEntry> Read(Guid jellyfinItemId, ArrProviderKind providerKind)
    {
        return Read(GetRecordId(jellyfinItemId, providerKind));
    }

    /// <summary>
    /// Reads the metadata state for a record identifier. A valid envelope whose
    /// payload violates the documented invariants is discarded as rebuildable.
    /// </summary>
    /// <param name="recordId">The state record identifier.</param>
    /// <returns>The state read outcome.</returns>
    public StateReadResult<MetadataStateEntry> Read(string recordId)
    {
        ArgumentException.ThrowIfNullOrEmpty(recordId);

        var result = _repository.Read<MetadataStateEntry>(StateAuthority.Cache, RecordKind, recordId);
        if (result.Status != StateReadStatus.Found || result.Value is null)
        {
            return result;
        }

        if (result.Value.Validate(out var reason))
        {
            return result;
        }

        TryDiscard(recordId);
        return StateResults.Discarded<MetadataStateEntry>(reason);
    }

    /// <summary>
    /// Writes a metadata state record atomically as rebuildable cache state.
    /// </summary>
    /// <param name="entry">The record to persist.</param>
    /// <exception cref="ArgumentNullException">The entry is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entry violates a documented invariant.</exception>
    public void Write(MetadataStateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.Validate(out var reason))
        {
            throw new ArgumentException(
                string.Concat("The metadata state is invalid: ", reason),
                nameof(entry));
        }

        _repository.Write(
            StateAuthority.Cache,
            RecordKind,
            GetRecordId(entry.JellyfinItemId, entry.ProviderKind),
            entry);
    }

    private void TryDiscard(string recordId)
    {
        try
        {
            var path = _repository.Paths.GetRecordPath(StateAuthority.Cache, RecordKind, recordId);
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (System.IO.IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
