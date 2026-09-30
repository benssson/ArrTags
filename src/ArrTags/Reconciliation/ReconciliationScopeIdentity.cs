using System;
using System.Collections.Generic;
using ArrTags.Configuration;

namespace ArrTags.Reconciliation;

/// <summary>
/// The bounded reconciliation scope identity the persisted cursor is valid for
/// (ADR-022 clause 5): the sorted enabled-library identifier set and the
/// supported item-type surface scope. A cursor recorded for a different scope is
/// reset to the start, so a removed or added library or a changed movie/episode
/// surface cannot leave the cursor pointing past unenumerated items.
/// </summary>
public sealed class ReconciliationScopeIdentity
{
    private readonly string[] _enabledLibraryIds;

    private ReconciliationScopeIdentity(string[] enabledLibraryIds, bool moviePosters, bool episodePosters)
    {
        _enabledLibraryIds = enabledLibraryIds;
        MoviePosters = moviePosters;
        EpisodePosters = episodePosters;
    }

    /// <summary>
    /// Gets the normalized, ordinally sorted, distinct enabled-library
    /// identifiers. An empty set means no library restriction.
    /// </summary>
    public IReadOnlyList<string> EnabledLibraryIds => _enabledLibraryIds;

    /// <summary>
    /// Gets a value indicating whether movie poster reconciliation is in scope.
    /// </summary>
    public bool MoviePosters { get; }

    /// <summary>
    /// Gets a value indicating whether episode poster reconciliation is in scope.
    /// </summary>
    public bool EpisodePosters { get; }

    /// <summary>
    /// Creates the scope identity from the current validated configuration
    /// snapshot.
    /// </summary>
    /// <param name="snapshot">The current public configuration snapshot.</param>
    /// <returns>The normalized scope identity.</returns>
    /// <exception cref="ArgumentNullException">The snapshot is <see langword="null"/>.</exception>
    public static ReconciliationScopeIdentity From(PluginConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.EnabledLibraries)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                distinct.Add(entry.Trim());
            }
        }

        var normalized = new string[distinct.Count];
        distinct.CopyTo(normalized);
        Array.Sort(normalized, StringComparer.Ordinal);

        return new ReconciliationScopeIdentity(normalized, snapshot.BadgeMoviePosters, snapshot.BadgeEpisodePosters);
    }

    /// <summary>
    /// Determines whether a persisted cursor record belongs to this scope. The
    /// recorded library identifiers are compared as a normalized set so a
    /// differently ordered but equivalent record is not treated as a scope
    /// change.
    /// </summary>
    /// <param name="cursor">The persisted cursor record.</param>
    /// <returns><see langword="true"/> when the record's scope matches.</returns>
    /// <exception cref="ArgumentNullException">The cursor is <see langword="null"/>.</exception>
    public bool Matches(ReconciliationCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (MoviePosters != cursor.MoviePosters || EpisodePosters != cursor.EpisodePosters)
        {
            return false;
        }

        var recorded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in cursor.EnabledLibraryIds)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                recorded.Add(entry.Trim());
            }
        }

        if (recorded.Count != _enabledLibraryIds.Length)
        {
            return false;
        }

        foreach (var libraryId in _enabledLibraryIds)
        {
            if (!recorded.Contains(libraryId))
            {
                return false;
            }
        }

        return true;
    }
}
