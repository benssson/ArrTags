namespace ArrTags.Updates;

/// <summary>
/// The bounded, provider-neutral reason a Jellyfin library change was converted
/// into an update work hint. It mirrors the library change the hosted lifecycle
/// observed and carries no provider, connection, or credential data.
/// </summary>
public enum LibraryWorkReason
{
    /// <summary>An item was added to an eligible library.</summary>
    Added,

    /// <summary>An item was updated.</summary>
    Updated,

    /// <summary>An item was removed from a library.</summary>
    Removed,
}
