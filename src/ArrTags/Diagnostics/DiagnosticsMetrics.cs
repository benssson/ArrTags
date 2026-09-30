using System;
using System.Threading;
using ArrTags.Matching;
using ArrTags.Providers;
using ArrTags.Rendering;

namespace ArrTags.Diagnostics;

/// <summary>
/// The bounded, thread-safe, secret-free process-lifetime diagnostics counters
/// (ADR-025 clauses 3 and 4). Instrumentation at the existing bounded boundaries
/// records counts only - never an item identity, item name, path, provider
/// payload, credential, or secret value - and <see cref="Capture"/> assembles
/// the fixed-shape <see cref="DiagnosticsSnapshot"/>. The counters are in-memory
/// and process-lifetime; they reset on restart and are never persisted.
/// </summary>
/// <remarks>
/// The counter set is the ADR-025 clause 3 contract: matching failures by
/// bounded <see cref="MediaMatchStatus"/> classification, cache hits and misses,
/// render failures by bounded <see cref="RenderFailureReason"/> classification,
/// stale metadata transitions, and the bounded per-connection provider health
/// slots. Increment operations are lock-free and allocation-free; only
/// <see cref="Capture"/>, which is not a hot path, allocates the snapshot.
/// </remarks>
public sealed class DiagnosticsMetrics
{
    private readonly long[] _renderFailures = new long[Enum.GetValues<RenderFailureReason>().Length];
    private readonly int[] _providerHealth = new int[Enum.GetValues<ArrProviderKind>().Length];

    private long _matchingNotFound;
    private long _matchingAmbiguous;
    private long _matchingUnsupported;
    private long _cacheHits;
    private long _cacheMisses;
    private long _staleMetadataTransitions;

    /// <summary>
    /// Records one provider inventory read served from a retained cache entry
    /// without a provider library read.
    /// </summary>
    public void RecordCacheHit()
    {
        Interlocked.Increment(ref _cacheHits);
    }

    /// <summary>
    /// Records one provider inventory read that the cache could not serve and
    /// which had to reach the provider (a cold population or a direct read).
    /// </summary>
    public void RecordCacheMiss()
    {
        Interlocked.Increment(ref _cacheMisses);
    }

    /// <summary>
    /// Records one matching outcome by bounded classification. Only the
    /// non-matched <see cref="MediaMatchStatus.NotFound"/>,
    /// <see cref="MediaMatchStatus.Ambiguous"/>, and
    /// <see cref="MediaMatchStatus.Unsupported"/> outcomes are failures and are
    /// counted; <see cref="MediaMatchStatus.Matched"/> and
    /// <see cref="MediaMatchStatus.Stale"/> are not failures and are ignored.
    /// </summary>
    /// <param name="status">The bounded matching outcome.</param>
    public void RecordMatchingFailure(MediaMatchStatus status)
    {
        switch (status)
        {
            case MediaMatchStatus.NotFound:
                Interlocked.Increment(ref _matchingNotFound);
                break;
            case MediaMatchStatus.Ambiguous:
                Interlocked.Increment(ref _matchingAmbiguous);
                break;
            case MediaMatchStatus.Unsupported:
                Interlocked.Increment(ref _matchingUnsupported);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Records one render failure by its bounded, non-secret classification. A
    /// value outside the declared <see cref="RenderFailureReason"/> set is
    /// ignored rather than throwing on the artwork path.
    /// </summary>
    /// <param name="reason">The bounded render failure classification.</param>
    public void RecordRenderFailure(RenderFailureReason reason)
    {
        var index = (int)reason;
        if (index < 0 || index >= _renderFailures.Length)
        {
            return;
        }

        Interlocked.Increment(ref _renderFailures[index]);
    }

    /// <summary>
    /// Records the last observed health of one provider connection. The health
    /// slots are bounded by the declared <see cref="ArrProviderKind"/> set, so
    /// the recording surface can never grow with library size; an undefined
    /// health value or provider kind is ignored.
    /// </summary>
    /// <param name="provider">The provider family the observation is scoped to.</param>
    /// <param name="health">The bounded observed connection health.</param>
    public void RecordProviderHealth(ArrProviderKind provider, ArrConnectionHealth health)
    {
        if (!Enum.IsDefined(health))
        {
            return;
        }

        var index = (int)provider;
        if (index < 0 || index >= _providerHealth.Length)
        {
            return;
        }

        Volatile.Write(ref _providerHealth[index], (int)health);
    }

    /// <summary>
    /// Records one fresh metadata record transitioning to the explicit bounded
    /// stale last-known-good state at the reconciliation boundary.
    /// </summary>
    public void RecordStaleMetadata()
    {
        Interlocked.Increment(ref _staleMetadataTransitions);
    }

    /// <summary>
    /// Captures the fixed-shape snapshot from the current counter values and the
    /// supplied bounded queue observations. It performs no lock, I/O, provider,
    /// render, or library work beyond copying the counter values.
    /// </summary>
    /// <param name="queueDepth">The bounded pending work-queue depth to report.</param>
    /// <param name="queueInFlight">The bounded work-queue in-flight count to report.</param>
    /// <returns>The bounded, secret-free diagnostics snapshot.</returns>
    public DiagnosticsSnapshot Capture(int queueDepth, int queueInFlight)
    {
        return new DiagnosticsSnapshot(
            queueDepth,
            queueInFlight,
            ReadProviderHealth(ArrProviderKind.Sonarr),
            ReadProviderHealth(ArrProviderKind.Radarr),
            new MatchingFailureCounts(
                Interlocked.Read(ref _matchingNotFound),
                Interlocked.Read(ref _matchingAmbiguous),
                Interlocked.Read(ref _matchingUnsupported)),
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheMisses),
            CreateRenderFailureCounts(),
            Interlocked.Read(ref _staleMetadataTransitions));
    }

    private ArrConnectionHealth ReadProviderHealth(ArrProviderKind provider)
    {
        var index = (int)provider;
        if (index < 0 || index >= _providerHealth.Length)
        {
            return ArrConnectionHealth.Unknown;
        }

        return (ArrConnectionHealth)Volatile.Read(ref _providerHealth[index]);
    }

    private RenderFailureCounts CreateRenderFailureCounts()
    {
        return new RenderFailureCounts(
            ReadRenderFailure(RenderFailureReason.InvalidRequest),
            ReadRenderFailure(RenderFailureReason.MalformedSource),
            ReadRenderFailure(RenderFailureReason.SourceByteLimitExceeded),
            ReadRenderFailure(RenderFailureReason.SourceDimensionLimitExceeded),
            ReadRenderFailure(RenderFailureReason.OutputDimensionLimitExceeded),
            ReadRenderFailure(RenderFailureReason.OutputByteLimitExceeded),
            ReadRenderFailure(RenderFailureReason.ColorPolicyInvalid),
            ReadRenderFailure(RenderFailureReason.ContrastTooLow),
            ReadRenderFailure(RenderFailureReason.DecodeFailed),
            ReadRenderFailure(RenderFailureReason.UnsupportedInput),
            ReadRenderFailure(RenderFailureReason.UnsupportedColorProfile),
            ReadRenderFailure(RenderFailureReason.FontUnavailable),
            ReadRenderFailure(RenderFailureReason.FontInvalid),
            ReadRenderFailure(RenderFailureReason.LayoutFailed),
            ReadRenderFailure(RenderFailureReason.EncodeFailed),
            ReadRenderFailure(RenderFailureReason.NativeAssetUnavailable),
            ReadRenderFailure(RenderFailureReason.RenderError),
            ReadRenderFailure(RenderFailureReason.Cancelled));
    }

    private long ReadRenderFailure(RenderFailureReason reason)
    {
        var index = (int)reason;
        if (index < 0 || index >= _renderFailures.Length)
        {
            return 0;
        }

        return Interlocked.Read(ref _renderFailures[index]);
    }
}
