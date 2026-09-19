namespace ArrTags.Artwork;

/// <summary>
/// The provider-neutral Jellyfin image surface type tracked by ArrTags artwork
/// provenance. The enum is intentionally limited to the V1 surface: the
/// unindexed <c>Primary</c> poster for Movie and Episode items (ADR-006 and
/// ADR-009). Alternate and indexed surfaces are out of V1 and are added only
/// when a later task defines their policy.
/// </summary>
public enum ArtworkImageType
{
    /// <summary>The single primary poster surface.</summary>
    Primary,
}
