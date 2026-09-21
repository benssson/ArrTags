using System;

namespace ArrTags.Updates;

/// <summary>
/// A bounded, provider-neutral unit of update work produced by a Jellyfin
/// library event. It carries only the Jellyfin item id, the reason the item is
/// relevant, and the safe configuration generation that was active when the
/// event was observed. It never carries an API key, secret lease, credential,
/// provider DTO, connection secret, path, or unbounded payload, so it is safe to
/// queue, retain, and log.
/// </summary>
public readonly struct LibraryWorkHint : IEquatable<LibraryWorkHint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWorkHint"/> struct.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier from the library event.</param>
    /// <param name="reason">The bounded reason the item is relevant.</param>
    /// <param name="configurationVersion">The safe configuration generation observed at the event boundary.</param>
    public LibraryWorkHint(Guid itemId, LibraryWorkReason reason, long configurationVersion)
    {
        ItemId = itemId;
        Reason = reason;
        ConfigurationVersion = configurationVersion;
    }

    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the bounded reason this item is a candidate for update work.
    /// </summary>
    public LibraryWorkReason Reason { get; }

    /// <summary>
    /// Gets the configuration generation observed at the library-event boundary.
    /// A worker must still resolve the current public snapshot and deadline; this
    /// is only a safe freshness hint and never a credential.
    /// </summary>
    public long ConfigurationVersion { get; }

    /// <summary>
    /// Determines whether two hints are equal.
    /// </summary>
    /// <param name="left">The left hint.</param>
    /// <param name="right">The right hint.</param>
    /// <returns><see langword="true"/> when every hint field is equal.</returns>
    public static bool operator ==(LibraryWorkHint left, LibraryWorkHint right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two hints differ.
    /// </summary>
    /// <param name="left">The left hint.</param>
    /// <param name="right">The right hint.</param>
    /// <returns><see langword="true"/> when any hint field differs.</returns>
    public static bool operator !=(LibraryWorkHint left, LibraryWorkHint right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Determines whether this hint equals another hint.
    /// </summary>
    /// <param name="other">The other hint.</param>
    /// <returns><see langword="true"/> when every hint field is equal.</returns>
    public bool Equals(LibraryWorkHint other)
    {
        return ItemId == other.ItemId
            && Reason == other.Reason
            && ConfigurationVersion == other.ConfigurationVersion;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LibraryWorkHint other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ItemId, Reason, ConfigurationVersion);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return FormattableString.Invariant($"LibraryWorkHint({ItemId:N}, {Reason}, v{ConfigurationVersion})");
    }
}
