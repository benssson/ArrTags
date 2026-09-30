using ArrTags.Providers;

namespace ArrTags.Diagnostics;

/// <summary>
/// The fixed-shape, bounded, secret-free diagnostics snapshot (ADR-025 clauses 3
/// and 4). Every value is a process-lifetime count, a bounded queue depth, or a
/// bounded <see cref="ArrConnectionHealth"/> enum value. The snapshot contains
/// no path, item name, item identifier, item identifier list, provider payload,
/// credential, secret value, or unbounded collection, and no per-item array.
/// </summary>
/// <remarks>
/// The counter set is a contract: update-queue depth and in-flight work,
/// provider connection health, matching failures by bounded classification,
/// cache hits and misses, render failures by bounded classification, and stale
/// metadata transitions. Adding or removing a counter requires a new decision
/// (ADR-025 clause 3). All counters are process-lifetime and reset on restart;
/// the snapshot reports current process state, never durable history.
/// </remarks>
public sealed class DiagnosticsSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsSnapshot"/> class.
    /// </summary>
    /// <param name="queueDepth">The current bounded pending work-queue depth.</param>
    /// <param name="queueInFlight">The current bounded work-queue in-flight count.</param>
    /// <param name="sonarrHealth">The last observed Sonarr connection health.</param>
    /// <param name="radarrHealth">The last observed Radarr connection health.</param>
    /// <param name="matchingFailures">The process-lifetime matching-failure counters.</param>
    /// <param name="cacheHits">The process-lifetime count of provider inventory reads served by the cache.</param>
    /// <param name="cacheMisses">The process-lifetime count of provider inventory reads that had to reach the provider.</param>
    /// <param name="renderFailures">The process-lifetime render-failure counters by bounded classification.</param>
    /// <param name="staleMetadataTransitions">The process-lifetime count of fresh metadata records marked as bounded stale last-known-good.</param>
    /// <exception cref="System.ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public DiagnosticsSnapshot(
        int queueDepth,
        int queueInFlight,
        ArrConnectionHealth sonarrHealth,
        ArrConnectionHealth radarrHealth,
        MatchingFailureCounts matchingFailures,
        long cacheHits,
        long cacheMisses,
        RenderFailureCounts renderFailures,
        long staleMetadataTransitions)
    {
        System.ArgumentNullException.ThrowIfNull(matchingFailures);
        System.ArgumentNullException.ThrowIfNull(renderFailures);

        QueueDepth = queueDepth;
        QueueInFlight = queueInFlight;
        SonarrHealth = sonarrHealth;
        RadarrHealth = radarrHealth;
        MatchingFailures = matchingFailures;
        CacheHits = cacheHits;
        CacheMisses = cacheMisses;
        RenderFailures = renderFailures;
        StaleMetadataTransitions = staleMetadataTransitions;
    }

    /// <summary>
    /// Gets the current bounded pending update-queue depth.
    /// </summary>
    public int QueueDepth { get; }

    /// <summary>
    /// Gets the current bounded update-queue in-flight count.
    /// </summary>
    public int QueueInFlight { get; }

    /// <summary>
    /// Gets the last observed Sonarr connection health.
    /// </summary>
    public ArrConnectionHealth SonarrHealth { get; }

    /// <summary>
    /// Gets the last observed Radarr connection health.
    /// </summary>
    public ArrConnectionHealth RadarrHealth { get; }

    /// <summary>
    /// Gets the process-lifetime matching-failure counters by bounded
    /// classification.
    /// </summary>
    public MatchingFailureCounts MatchingFailures { get; }

    /// <summary>
    /// Gets the process-lifetime count of provider inventory reads that a
    /// retained cache entry served without a provider library read.
    /// </summary>
    public long CacheHits { get; }

    /// <summary>
    /// Gets the process-lifetime count of provider inventory reads that could
    /// not be served by the cache and had to reach the provider.
    /// </summary>
    public long CacheMisses { get; }

    /// <summary>
    /// Gets the process-lifetime render-failure counters by bounded
    /// classification.
    /// </summary>
    public RenderFailureCounts RenderFailures { get; }

    /// <summary>
    /// Gets the process-lifetime count of metadata records marked as bounded
    /// stale last-known-good at the reconciliation boundary.
    /// </summary>
    public long StaleMetadataTransitions { get; }
}
