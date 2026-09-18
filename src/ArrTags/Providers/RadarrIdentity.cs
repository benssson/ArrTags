using System;
using System.Globalization;

namespace ArrTags.Providers;

/// <summary>
/// The connection-scoped Radarr identity of a matched movie and its current
/// imported file. Radarr keeps at most one current file per movie, so the file
/// identity is explicit but never plural.
/// </summary>
public sealed class RadarrIdentity : ArrRecordIdentity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RadarrIdentity"/> class.
    /// </summary>
    /// <param name="connectionId">The connection scope.</param>
    /// <param name="movieId">The Radarr-local movie identifier.</param>
    /// <param name="movieFileIdentity">The current movie-file identity, including explicit absence.</param>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The movie identifier is not positive.</exception>
    public RadarrIdentity(ArrConnectionId connectionId, int movieId, ArrFileIdentity movieFileIdentity)
        : base(connectionId, ArrProviderKind.Radarr)
    {
        ArgumentNullException.ThrowIfNull(movieFileIdentity);

        if (movieId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(movieId), movieId, "A Radarr movie identifier must be positive.");
        }

        MovieId = movieId;
        MovieFileIdentity = movieFileIdentity;
    }

    /// <summary>
    /// Gets the Radarr-local movie identifier.
    /// </summary>
    public int MovieId { get; }

    /// <summary>
    /// Gets the current movie-file identity, including explicit absence.
    /// </summary>
    public ArrFileIdentity MovieFileIdentity { get; }

    /// <inheritdoc />
    protected override bool EqualsCore(ArrRecordIdentity other)
    {
        var radarr = (RadarrIdentity)other;
        return MovieId == radarr.MovieId && MovieFileIdentity.Equals(radarr.MovieFileIdentity);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ConnectionId, ProviderKind, MovieId, MovieFileIdentity);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return "radarr|" + ConnectionId.Value + "|movie:" + MovieId.ToString(CultureInfo.InvariantCulture) + "|" + MovieFileIdentity;
    }
}
