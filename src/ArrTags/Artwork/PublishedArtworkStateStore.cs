using System;
using System.Collections.Generic;
using System.Globalization;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// Persists and reads <see cref="PublishedArtworkState"/> through the versioned
/// authoritative state boundary. The state is integrity-tagged, atomically
/// written, and quarantined on corruption; it is never treated as ordinary
/// cache and is never blindly replayed.
/// </summary>
public sealed class PublishedArtworkStateStore
{
    /// <summary>
    /// The authoritative state record kind for published artwork state.
    /// </summary>
    public const string RecordKind = "artwork-state";

    private readonly StateRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishedArtworkStateStore"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository under the plugin data root.</param>
    /// <exception cref="ArgumentNullException">The repository is <see langword="null"/>.</exception>
    public PublishedArtworkStateStore(StateRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>
    /// Gets the stable, traversal-safe state record identifier for an item and surface.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface.</param>
    /// <returns>The record identifier.</returns>
    /// <exception cref="ArgumentNullException">The surface is <see langword="null"/>.</exception>
    public static string GetRecordId(Guid jellyfinItemId, ArtworkImageSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return string.Concat(
            jellyfinItemId.ToString("N", CultureInfo.InvariantCulture),
            "-",
            surface.Key);
    }

    /// <summary>
    /// Reads the artwork state for an item and surface.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item identifier.</param>
    /// <param name="surface">The image surface.</param>
    /// <returns>The state read outcome.</returns>
    public StateReadResult<PublishedArtworkState> Read(Guid jellyfinItemId, ArtworkImageSurface surface)
    {
        return Read(GetRecordId(jellyfinItemId, surface));
    }

    /// <summary>
    /// Reads the artwork state for a record identifier. A valid envelope whose
    /// payload violates the documented invariants is quarantined and reported as
    /// invalid rather than returned as current state.
    /// </summary>
    /// <param name="recordId">The state record identifier.</param>
    /// <returns>The state read outcome.</returns>
    public StateReadResult<PublishedArtworkState> Read(string recordId)
    {
        ArgumentException.ThrowIfNullOrEmpty(recordId);

        StateReadResult<PublishedArtworkState> result;
        try
        {
            result = _repository.Read<PublishedArtworkState>(StateAuthority.Authoritative, RecordKind, recordId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Quarantine(recordId, "The artwork state record could not be deserialized.");
        }

        if (result.Status != StateReadStatus.Found || result.Value is null)
        {
            return result;
        }

        if (result.Value.Validate(out var reason))
        {
            return result;
        }

        return Quarantine(recordId, reason);
    }

    /// <summary>
    /// Enumerates the valid published-artwork state records in a bounded,
    /// deterministic order. An invalid record is quarantined and omitted rather
    /// than returned as current state.
    /// </summary>
    /// <param name="maxRecords">The bounded maximum number of records; defaults to the repository bound.</param>
    /// <returns>The valid state records.</returns>
    public IReadOnlyList<PublishedArtworkState> Enumerate(int maxRecords = StateRepository.MaxEnumerationRecords)
    {
        return _repository.Enumerate<PublishedArtworkState>(
            StateAuthority.Authoritative,
            RecordKind,
            maxRecords);
    }

    /// <summary>
    /// Writes an artwork state record atomically as authoritative state.
    /// </summary>
    /// <param name="state">The state to persist.</param>
    /// <exception cref="ArgumentNullException">The state is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The state violates a documented invariant.</exception>
    public void Write(PublishedArtworkState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Validate(out var reason))
        {
            throw new ArgumentException(
                string.Concat("The published artwork state is invalid: ", reason),
                nameof(state));
        }

        _repository.Write(
            StateAuthority.Authoritative,
            RecordKind,
            GetRecordId(state.JellyfinItemId, state.ImageSurface),
            state,
            state.IsTerminal);
    }

    private StateReadResult<PublishedArtworkState> Quarantine(string recordId, string reason)
    {
        StateQuarantine.TryQuarantine(
            _repository.Paths,
            StateAuthority.Authoritative,
            RecordKind,
            recordId,
            DateTimeOffset.UtcNow,
            reason,
            out _);
        return StateResults.Quarantined<PublishedArtworkState>(reason);
    }
}
