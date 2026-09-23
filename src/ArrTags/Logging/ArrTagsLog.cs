using System;
using ArrTags.PluginLifecycle;
using Microsoft.Extensions.Logging;

namespace ArrTags.Logging;

/// <summary>
/// The default <see cref="IArrTagsLog{T}"/> (ADR-020). It resolves the host
/// <see cref="ILogger{T}"/> for the calling type, gates every call on the
/// plugin-owned <see cref="ILogVerbosityGate"/> read from the current
/// configuration snapshot, and applies the shared <see cref="LogThrottle"/>. The
/// emitted data shape is identical at every verbosity level: raising verbosity
/// changes only whether a bounded message is written, never what fields it
/// carries, so a Debug/Trace raise cannot expand a redacted value into a
/// secret-bearing one (ADR-020 clause 4).
/// </summary>
/// <typeparam name="T">The calling boundary type that owns the log category.</typeparam>
public sealed class ArrTagsLog<T> : IArrTagsLog<T>
{
    private const string MessageTemplate = "{ArrTagsMessage}";

    private const string SuppressionTemplate =
        "Suppressed {SuppressedCount} repeated '{EventName}' ArrTags log messages.";

    private readonly ILogger<T> _logger;
    private readonly ILogVerbosityGate _gate;
    private readonly LogThrottle _throttle;
    private readonly string _category;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrTagsLog{T}"/> class.
    /// </summary>
    /// <param name="logger">The host logger for the calling type.</param>
    /// <param name="gate">The plugin-owned verbosity gate.</param>
    /// <param name="throttle">The shared bounded repetition suppressor.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArrTagsLog(ILogger<T> logger, ILogVerbosityGate gate, LogThrottle throttle)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _category = typeof(T).FullName ?? typeof(T).Name;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level)
    {
        return level != LogLevel.None && _gate.IsEnabled(level) && _logger.IsEnabled(level);
    }

    /// <inheritdoc />
    public void Write(LogLevel level, ArrTagsLogEvent logEvent, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsEnabled(level))
        {
            return;
        }

        var decision = _throttle.Acquire(_category, logEvent, out var suppressedCount);
        switch (decision)
        {
            case LogThrottleDecision.Suppress:
                return;

            case LogThrottleDecision.EmitSuppressionSummary:
                _logger.Log(level, (int)logEvent, SuppressionTemplate, suppressedCount, logEvent);
                return;

            default:
                _logger.Log(level, (int)logEvent, MessageTemplate, message);
                return;
        }
    }
}
