namespace ArrTags.Artwork;

/// <summary>
/// The guarded ownership state of one item/image surface. It controls whether
/// ArrTags may publish, restore, or remove the active image. The blocked states
/// (<see cref="OwnershipLost"/>, <see cref="OwnershipUnknown"/>, and
/// <see cref="RestoreBlocked"/>) are never automatically re-baselined; an
/// explicit administrative action would have to start a new session.
/// </summary>
public enum ArtworkPublicationState
{
    /// <summary>No ArrTags publication session exists and no image is owned.</summary>
    NotPublished,

    /// <summary>ArrTags owns the active derived image for the surface.</summary>
    Published,

    /// <summary>The active image changed after publication; ArrTags no longer owns it.</summary>
    OwnershipLost,

    /// <summary>Ownership could not be observed or compared; ArrTags fails closed.</summary>
    OwnershipUnknown,

    /// <summary>Restoration was requested and must be re-observed before mutation.</summary>
    RestorePending,

    /// <summary>The recorded source was restored, or the ArrTags image was removed.</summary>
    Restored,

    /// <summary>Restoration could not be completed or verified; no further automatic mutation.</summary>
    RestoreBlocked,

    /// <summary>The Jellyfin item was removed; no image mutation is permitted.</summary>
    Removed,
}
