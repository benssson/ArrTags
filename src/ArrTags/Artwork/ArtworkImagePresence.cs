namespace ArrTags.Artwork;

/// <summary>
/// The presence of an image on one Jellyfin image surface. Absence is an
/// identity value, not an error: an absent baseline is recorded explicitly so
/// restoration can remove an ArrTags image instead of guessing at a source.
/// </summary>
public enum ArtworkImagePresence
{
    /// <summary>The surface has an active image.</summary>
    Present,

    /// <summary>The surface has no image.</summary>
    Absent,
}
