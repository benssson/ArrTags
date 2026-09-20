using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArrTags.Artwork;

/// <summary>
/// The plugin lifecycle coordination boundary. It owns the durable active
/// lifecycle fence, drains existing publication work to a terminal result before
/// guarded restoration, restores a still-owned published surface through the
/// supported item-image APIs, and tombstones a confirmed item removal without
/// issuing any image mutation. It is the only boundary the hosted lifecycle
/// service and the plugin uninstall hook call, so the service can be exercised
/// with an injectable double and no live host.
/// </summary>
public interface IArtworkLifecycleCoordinator
{
    /// <summary>
    /// Clears a stale non-normal fence when the host has loaded the plugin
    /// active. This does not restore anything; the per-subject state and
    /// operation records still guard against unsafe work.
    /// </summary>
    void ResetStaleFence();

    /// <summary>
    /// Records the fence and, for a disable or uninstall fence, reconciles every
    /// existing non-terminal operation to a terminal result before restoring
    /// every still-owned published surface. The drain is bounded by the supplied
    /// cancellation signal and never throws for an expected failure.
    /// </summary>
    /// <param name="fence">The disable or uninstall fence to drain.</param>
    /// <param name="cancellationToken">The bounded cancellation signal.</param>
    /// <returns>The bounded lifecycle result.</returns>
    Task<ArtworkLifecycleResult> DrainAsync(
        ArtworkLifecycleFence fence,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the fence implied by the durable record and the host plugin
    /// state and drains it. It is a no-op when no disable or uninstall is
    /// implied, so a plain server shutdown does not restore artwork.
    /// </summary>
    /// <param name="cancellationToken">The bounded cancellation signal.</param>
    /// <returns>The bounded lifecycle result.</returns>
    Task<ArtworkLifecycleResult> DrainForHostShutdownAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads the item for one confirmed-removal hint. Once absence is
    /// confirmed, the in-flight operation is tombstoned and the plugin state is
    /// marked removed without any Jellyfin image call.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier from the hint.</param>
    /// <param name="surface">The image surface; V1 supports only the unindexed <c>Primary</c> surface.</param>
    /// <param name="cancellationToken">The cancellation signal.</param>
    /// <returns>The bounded item-removal result.</returns>
    Task<ArtworkRemovalResult> HandleItemRemovedAsync(
        Guid itemId,
        ArtworkImageSurface surface,
        CancellationToken cancellationToken);
}
