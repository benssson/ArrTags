using System;
using ArrTags.Configuration;

namespace ArrTags.Media;

/// <summary>
/// Applies the configured library scope and V1 badge-surface policy to a
/// canonical <see cref="MediaIdentity"/>. Library scope entries are Jellyfin
/// collection-folder/library identifiers; an empty set means no library
/// restriction. Series and Season are never V1 badge surfaces even though they
/// remain supported structural identities (ADR-006).
/// </summary>
public static class MediaEligibility
{
    /// <summary>
    /// Determines whether an item belongs to the configured library scope. An
    /// empty scope permits every library; a non-empty scope fails closed when the
    /// item's library cannot be resolved.
    /// </summary>
    /// <param name="identity">The item identity.</param>
    /// <param name="configuration">The validated configuration snapshot.</param>
    /// <returns><see langword="true"/> when the item is within the library scope.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool IsInLibraryScope(MediaIdentity identity, PluginConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(configuration);

        var scope = configuration.EnabledLibraries;
        if (scope.Count == 0)
        {
            return true;
        }

        if (identity.LibraryId is not Guid libraryId)
        {
            return false;
        }

        foreach (var entry in scope)
        {
            if (Guid.TryParse(entry, out var configuredLibraryId) && configuredLibraryId == libraryId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an item is a V1 badge-bearing surface. Only Movie and
    /// Episode posters can bear badges, and only when the corresponding surface is
    /// enabled in configuration; Series and Season remain structural and produce
    /// no badge.
    /// </summary>
    /// <param name="identity">The item identity.</param>
    /// <param name="configuration">The validated configuration snapshot.</param>
    /// <returns><see langword="true"/> when the item surface is badge-bearing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool IsBadgeSurface(MediaIdentity identity, PluginConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(configuration);

        return identity.ItemType switch
        {
            MediaItemType.Movie => configuration.BadgeMoviePosters,
            MediaItemType.Episode => configuration.BadgeEpisodePosters,
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether an item is both within the configured library scope, a
    /// badge-bearing surface, and backed by an eligible local file location.
    /// </summary>
    /// <param name="identity">The item identity.</param>
    /// <param name="configuration">The validated configuration snapshot.</param>
    /// <returns><see langword="true"/> when the item is eligible for a V1 badge.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool IsEligible(MediaIdentity identity, PluginConfigurationSnapshot configuration)
    {
        return IsInLibraryScope(identity, configuration)
            && IsBadgeSurface(identity, configuration)
            && MediaLocationEligibility.IsEligible(identity);
    }
}
