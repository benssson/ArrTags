using System;

namespace ArrTags.Media;

/// <summary>
/// The V1 policy that decides whether a Jellyfin item has an eligible local file
/// location for badge generation. It implements the fail-closed behavior
/// recorded in <c>docs/architecture.md</c> section 7 and ADR-008: remote,
/// virtual, offline, <c>.strm</c>, missing/fileless, and otherwise non-local
/// items are never eligible and therefore produce no badge. It never compares
/// Jellyfin and Arr paths and never uses a path as an identity key.
/// </summary>
/// <remarks>
/// An item whose location was never captured (<see cref="MediaIdentity.MediaLocation"/>
/// is <see langword="null"/>) is treated as not evaluated and remains eligible so
/// the policy only rejects locations that are positively known to be ineligible.
/// Production identities are always built by <see cref="MediaIdentityFactory"/>,
/// which captures the location summary.
/// </remarks>
public static class MediaLocationEligibility
{
    private const string StrmReason =
        "The item is a .strm reference, which is not an eligible local file for V1 badges.";

    private const string RemoteReason =
        "The item is remote, which is not an eligible local file for V1 badges.";

    private const string VirtualReason =
        "The item is virtual, which is not an eligible local file for V1 badges.";

    private const string OfflineReason =
        "The item is offline, which is not an eligible local file for V1 badges.";

    private const string UnknownLocationReason =
        "The item location is unknown and is not an eligible local file for V1 badges.";

    private const string NonLocalReason =
        "The item has no eligible local file source for V1 badges.";

    /// <summary>
    /// Determines whether an item has an eligible local file location for a V1
    /// badge.
    /// </summary>
    /// <param name="identity">The item identity to evaluate.</param>
    /// <returns><see langword="true"/> when the item is an eligible local file.</returns>
    /// <exception cref="ArgumentNullException">The identity is <see langword="null"/>.</exception>
    public static bool IsEligible(MediaIdentity identity)
    {
        return !TryGetIneligibleReason(identity, out _);
    }

    /// <summary>
    /// Attempts to classify why an item has no eligible local file location.
    /// </summary>
    /// <param name="identity">The item identity to evaluate.</param>
    /// <param name="reason">A bounded, path-free explanation when the item is ineligible; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the item is not an eligible local file.</returns>
    /// <exception cref="ArgumentNullException">The identity is <see langword="null"/>.</exception>
    public static bool TryGetIneligibleReason(MediaIdentity identity, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(identity);

        reason = null;

        var location = identity.MediaLocation;
        if (location is null)
        {
            return false;
        }

        if (location.IsStrm)
        {
            reason = StrmReason;
            return true;
        }

        if (location.Kind == MediaLocationKind.Remote || location.IsRemote)
        {
            reason = RemoteReason;
            return true;
        }

        if (location.Kind == MediaLocationKind.Virtual)
        {
            reason = VirtualReason;
            return true;
        }

        if (location.Kind == MediaLocationKind.Offline)
        {
            reason = OfflineReason;
            return true;
        }

        if (location.Kind == MediaLocationKind.Unknown)
        {
            reason = UnknownLocationReason;
            return true;
        }

        if (!location.IsFileProtocol)
        {
            reason = NonLocalReason;
            return true;
        }

        return false;
    }
}
