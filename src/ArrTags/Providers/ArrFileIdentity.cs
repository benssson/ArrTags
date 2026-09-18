using System;

namespace ArrTags.Providers;

/// <summary>
/// The explicit presence and local identifier of one current Arr file. A missing
/// file identity is represented as <see cref="Absent"/>; it is never encoded as
/// zero, negative, <see langword="null"/>, or an empty string. A Sonarr
/// episode-file identity is not owned by one episode: multiple episode
/// identities may reference the same file identifier.
/// </summary>
public sealed class ArrFileIdentity : IEquatable<ArrFileIdentity>
{
    private ArrFileIdentity(ArrFilePresence presence, int? fileId)
    {
        Presence = presence;
        FileId = fileId;
    }

    /// <summary>
    /// Gets the shared absent file identity.
    /// </summary>
    public static ArrFileIdentity Absent { get; } = new ArrFileIdentity(ArrFilePresence.Absent, null);

    /// <summary>
    /// Gets the explicit file presence.
    /// </summary>
    public ArrFilePresence Presence { get; }

    /// <summary>
    /// Gets the Arr-local file identifier when the file is present.
    /// </summary>
    public int? FileId { get; }

    /// <summary>
    /// Creates a present file identity with a positive Arr-local identifier.
    /// </summary>
    /// <param name="fileId">The positive Arr-local file identifier.</param>
    /// <returns>A present file identity.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The identifier is not positive.</exception>
    public static ArrFileIdentity Present(int fileId)
    {
        if (fileId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileId), fileId, "A present Arr file identifier must be positive.");
        }

        return new ArrFileIdentity(ArrFilePresence.Present, fileId);
    }

    /// <summary>
    /// Determines whether this file identity equals another file identity.
    /// </summary>
    /// <param name="other">The other file identity.</param>
    /// <returns><see langword="true"/> when the presence and identifier are equal.</returns>
    public bool Equals(ArrFileIdentity? other)
    {
        return other is not null && Presence == other.Presence && FileId == other.FileId;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as ArrFileIdentity);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Presence, FileId);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Presence == ArrFilePresence.Present
            ? "file:" + FileId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "file:absent";
    }
}
