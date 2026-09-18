namespace ArrTags.Media;

/// <summary>
/// The canonical summary of where a Jellyfin item's media lives. It mirrors
/// Jellyfin's location classification without exposing the external
/// <c>LocationType</c> enum, so a virtual, remote, or non-file item can be
/// rejected without becoming an Arr match key.
/// </summary>
public enum MediaLocationKind
{
    /// <summary>The location could not be classified.</summary>
    Unknown,

    /// <summary>The item is backed by a path on the file system.</summary>
    FileSystem,

    /// <summary>The item is remote, such as a stream or a network URL.</summary>
    Remote,

    /// <summary>The item is virtual and has no file, such as a missing episode.</summary>
    Virtual,

    /// <summary>The item's media is offline.</summary>
    Offline,
}
