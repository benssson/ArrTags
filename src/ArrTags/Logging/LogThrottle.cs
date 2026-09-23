using System;
using System.Collections.Concurrent;

namespace ArrTags.Logging;

/// <summary>
/// The plugin-owned, provider-neutral, thread-safe repetition suppressor that
/// bounds ArrTags log volume (ADR-020 clause 6). A message is admitted at most
/// <see cref="MaxEmissionsPerWindow"/> times per category and event within
/// <see cref="SuppressionWindow"/>; further occurrences are counted and one
/// bounded suppression summary is written when the window rolls over. The
/// tracking set is capped at <see cref="MaxTrackedKeys"/>, so the suppressor's
/// own memory is bounded independently of the number of messages. The bounds are
/// code-owned constants, not user-configurable: ADR-020 authorizes a new
/// configuration field only for the verbosity itself.
/// </summary>
public sealed class LogThrottle
{
    /// <summary>
    /// The maximum number of messages admitted per category and event within one
    /// suppression window.
    /// </summary>
    public const int MaxEmissionsPerWindow = 5;

    /// <summary>
    /// The maximum number of category/event keys tracked before the tracking set
    /// is reset. It bounds the suppressor's own memory.
    /// </summary>
    public const int MaxTrackedKeys = 256;

    /// <summary>
    /// The bounded window over which repeated messages are suppressed.
    /// </summary>
    public static readonly TimeSpan SuppressionWindow = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogThrottle"/> class.
    /// </summary>
    /// <param name="timeProvider">The optional time source; defaults to the system clock.</param>
    public LogThrottle(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets the number of category/event keys currently tracked. It never exceeds
    /// <see cref="MaxTrackedKeys"/>.
    /// </summary>
    public int TrackedKeyCount => _buckets.Count;

    /// <summary>
    /// Decides whether one bounded message is admitted. The decision is keyed on
    /// the category and the bounded event identity, never on message text.
    /// </summary>
    /// <param name="category">The host logger category.</param>
    /// <param name="logEvent">The bounded, code-owned event identity.</param>
    /// <param name="suppressedCount">The number of messages suppressed in the previous window when the decision is <see cref="LogThrottleDecision.EmitSuppressionSummary"/>; otherwise zero.</param>
    /// <returns>The bounded admission decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public LogThrottleDecision Acquire(string category, ArrTagsLogEvent logEvent, out int suppressedCount)
    {
        ArgumentNullException.ThrowIfNull(category);
        suppressedCount = 0;

        var key = category + "|" + logEvent.ToString();
        if (_buckets.Count >= MaxTrackedKeys && !_buckets.ContainsKey(key))
        {
            // The tracked set is bounded independently of the message count. A
            // reset is coarse but safe: it can only re-admit a bounded number of
            // messages, never grow without bound.
            _buckets.Clear();
        }

        var bucket = _buckets.GetOrAdd(key, static _ => new Bucket());
        var now = _time.GetUtcNow();

        lock (bucket)
        {
            if (bucket.WindowStartUtc == default || now - bucket.WindowStartUtc >= SuppressionWindow)
            {
                var previouslySuppressed = bucket.Suppressed;
                bucket.WindowStartUtc = now;
                bucket.Emitted = 1;
                bucket.Suppressed = 0;
                if (previouslySuppressed > 0)
                {
                    suppressedCount = previouslySuppressed;
                    return LogThrottleDecision.EmitSuppressionSummary;
                }

                return LogThrottleDecision.Emit;
            }

            if (bucket.Emitted < MaxEmissionsPerWindow)
            {
                bucket.Emitted++;
                return LogThrottleDecision.Emit;
            }

            bucket.Suppressed++;
            suppressedCount = bucket.Suppressed;
            return LogThrottleDecision.Suppress;
        }
    }

    private sealed class Bucket
    {
        public DateTimeOffset WindowStartUtc { get; set; }

        public int Emitted { get; set; }

        public int Suppressed { get; set; }
    }
}
