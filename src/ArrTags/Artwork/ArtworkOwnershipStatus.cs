namespace ArrTags.Artwork;

/// <summary>
/// The fail-closed result of comparing a fresh active-image observation with a
/// persisted expected identity. <see cref="Owned"/> means every required value
/// matched; <see cref="Changed"/> means the surface observably differs;
/// <see cref="Unknown"/> means the comparison could not be completed and must
/// never be treated as ownership.
/// </summary>
public enum ArtworkOwnershipStatus
{
    /// <summary>The observed identity matches the expected identity.</summary>
    Owned,

    /// <summary>The observed identity differs from the expected identity.</summary>
    Changed,

    /// <summary>The comparison could not be completed; ArrTags fails closed.</summary>
    Unknown,
}
