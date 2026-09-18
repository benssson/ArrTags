namespace ArrTags.Media;

/// <summary>
/// The supported Jellyfin structural item types that can be snapshotted into a
/// canonical <see cref="MediaIdentity"/>. V1 badge-bearing surfaces are Movie
/// and Episode posters; Series and Season are structural and contextual only.
/// </summary>
public enum MediaItemType
{
    /// <summary>A movie item.</summary>
    Movie,

    /// <summary>A series item.</summary>
    Series,

    /// <summary>A season item.</summary>
    Season,

    /// <summary>An episode item.</summary>
    Episode,
}
