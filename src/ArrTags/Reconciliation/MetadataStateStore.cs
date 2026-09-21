using System;
using System.Collections.Generic;
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
    /// Enumerates a bounded number of valid metadata state records. The scan is
    /// bounded by <paramref name="maxRecords"/> and every returned record has
    /// passed the same semantic validation as <see cref="Read(string)"/>; a
    /// corrupt or invalid cache record is discarded by the repository scan and
    /// omitted. The enumeration is used by the bounded webhook
    /// provider-record-to-Jellyfin resolution (ADR-012) and must never be used
    /// to drive unbounded work.
    /// </summary>
    /// <param name="maxRecords">The bounded maximum number of records to inspect.</param>
    /// <returns>The valid records in the repository's bounded deterministic order.</returns>
    public IReadOnlyList<MetadataStateEntry> Enumerate(int maxRecords)
    {
        if (maxRecords <= 0)
        {
            return Array.Empty<MetadataStateEntry>();
        }

        var records = _repository.Enumerate<MetadataStateEntry>(StateAuthority.Cache, RecordKind, maxRecords);
        var valid = new List<MetadataStateEntry>(records.Count);
        foreach (var record in records)
        {
            if (record.Validate(out _))
            {
                valid.Add(record);
            }
        }

        return valid;
    }

    /// <summary>
    /// Prunes metadata state records whose bounded last-known-good window has
    /// ended. Metadata usability is governed by freshness, not by the render
    /// work-cache or authoritative-provenance eviction policy; a fresh or still
    /// usable stale record is never removed here. The scan is bounded and a
    /// corrupt record is discarded by <see cref="Read(string)"/> as usual.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The number of removed records.</returns>
    public int ApplyRetention(DateTimeOffset now)
    {
        var directory = _repository.Paths.GetKindDirectory(StateAuthority.Cache, RecordKind);
        if (!System.IO.Directory.Exists(directory))
        {
            return 0;
        }

        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(directory, "*.json", System.IO.SearchOption.TopDirectoryOnly);
        }
        catch (System.IO.IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in files)
        {
            var recordId = System.IO.Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(recordId))
            {
                continue;
            }

            var read = Read(recordId);
            if (read.Status != StateReadStatus.Found || read.Value is null)
            {
                // An invalid cache record was already discarded by Read; a
                // missing record has nothing to remove.
                continue;
            }

            if (!read.Value.IsExpired(now))
            {
                continue;
            }

            TryDiscard(recordId);
            removed++;
        }

        return removed;
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
