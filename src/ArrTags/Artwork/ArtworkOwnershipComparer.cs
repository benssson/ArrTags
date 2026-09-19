using System;

namespace ArrTags.Artwork;

/// <summary>
/// Compares a fresh active-image observation with a persisted expected identity
/// using the fail-closed ADR-002 rule. The surface must match; presence must
/// match; a present surface requires a matching content hash; and every recorded
/// Jellyfin value must still match when it is observable. A missing required
/// hash or an unavailable observation is always <see cref="ArtworkOwnershipStatus.Unknown"/>,
/// never ownership. A path never participates because Jellyfin may reuse a path
/// for a different image.
/// </summary>
public static class ArtworkOwnershipComparer
{
    /// <summary>
    /// Compares an expected identity with a fresh observation.
    /// </summary>
    /// <param name="expected">The persisted expected identity, or <see langword="null"/> when none is recorded.</param>
    /// <param name="observed">The fresh observation, or <see langword="null"/> when it could not be made.</param>
    /// <param name="observedAt">The comparison time.</param>
    /// <returns>The fail-closed comparison outcome.</returns>
    public static ArtworkOwnershipObservation Compare(
        ActiveImageIdentity? expected,
        ActiveImageIdentity? observed,
        DateTimeOffset observedAt)
    {
        if (expected is null)
        {
            return Unknown("No expected image identity is recorded.", observed, observedAt);
        }

        if (observed is null)
        {
            return Unknown("The active image could not be observed.", null, observedAt);
        }

        if (!expected.Surface.Equals(observed.Surface))
        {
            return Unknown("The observed image surface does not match the expected surface.", observed, observedAt);
        }

        if (expected.Presence != observed.Presence)
        {
            return Changed("The active image presence changed.", observed, observedAt);
        }

        if (expected.Presence == ArtworkImagePresence.Absent)
        {
            return Owned("The recorded surface remains absent.", observed, observedAt);
        }

        if (expected.ContentSha256 is null || observed.ContentSha256 is null)
        {
            return Unknown("A required active image content hash is unavailable.", observed, observedAt);
        }

        if (!string.Equals(expected.ContentSha256, observed.ContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Changed("The active image content hash changed.", observed, observedAt);
        }

        if (TryFindMetadataMismatch(expected, observed, out var mismatch))
        {
            return Changed(mismatch, observed, observedAt);
        }

        return Owned("The observed active image matches the expected identity.", observed, observedAt);
    }

    private static bool TryFindMetadataMismatch(
        ActiveImageIdentity expected,
        ActiveImageIdentity observed,
        out string reason)
    {
        if (expected.ByteLength is { } expectedLength
            && observed.ByteLength is { } observedLength
            && expectedLength != observedLength)
        {
            reason = "A recorded Jellyfin image byte length no longer matches.";
            return true;
        }

        if (expected.Width is { } expectedWidth
            && observed.Width is { } observedWidth
            && expectedWidth != observedWidth)
        {
            reason = "A recorded Jellyfin image width no longer matches.";
            return true;
        }

        if (expected.Height is { } expectedHeight
            && observed.Height is { } observedHeight
            && expectedHeight != observedHeight)
        {
            reason = "A recorded Jellyfin image height no longer matches.";
            return true;
        }

        if (expected.DateModifiedUtc is { } expectedModified
            && observed.DateModifiedUtc is { } observedModified
            && expectedModified.UtcDateTime != observedModified.UtcDateTime)
        {
            reason = "A recorded Jellyfin image modification time no longer matches.";
            return true;
        }

        if (expected.JellyfinImageTag is { } expectedTag
            && observed.JellyfinImageTag is { } observedTag
            && !string.Equals(expectedTag, observedTag, StringComparison.Ordinal))
        {
            reason = "A recorded Jellyfin image tag no longer matches.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static ArtworkOwnershipObservation Owned(string reason, ActiveImageIdentity? observed, DateTimeOffset at)
    {
        return new ArtworkOwnershipObservation(ArtworkOwnershipStatus.Owned, reason, observed, at);
    }

    private static ArtworkOwnershipObservation Changed(string reason, ActiveImageIdentity? observed, DateTimeOffset at)
    {
        return new ArtworkOwnershipObservation(ArtworkOwnershipStatus.Changed, reason, observed, at);
    }

    private static ArtworkOwnershipObservation Unknown(string reason, ActiveImageIdentity? observed, DateTimeOffset at)
    {
        return new ArtworkOwnershipObservation(ArtworkOwnershipStatus.Unknown, reason, observed, at);
    }
}
