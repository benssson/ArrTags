using Microsoft.Extensions.Logging;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The plugin-owned verbosity gate (ADR-020 clause 3). It reads the effective
/// verbosity from the current configuration snapshot on every call, so a
/// replaced configuration applies without a host restart, and decides whether a
/// log level is enabled for ArrTags. It is provider-neutral: ArrTags resolves
/// <see cref="ILogger{T}"/>/<see cref="ILoggerFactory"/> through plugin DI and
/// does not register a custom <see cref="ILoggerProvider"/>/sink or replace the
/// host <see cref="ILoggerFactory"/>.
/// </summary>
public interface ILogVerbosityGate
{
    /// <summary>
    /// Gets the minimum enabled <see cref="LogLevel"/> for the current
    /// configuration snapshot. <see cref="LogLevel.None"/> means ArrTags is
    /// configured to log nothing.
    /// </summary>
    LogLevel EffectiveLevel { get; }

    /// <summary>
    /// Returns whether the supplied level is enabled for ArrTags at the current
    /// verbosity.
    /// </summary>
    /// <param name="level">The candidate log level.</param>
    /// <returns><see langword="true"/> when ArrTags should emit the level; otherwise <see langword="false"/>.</returns>
    bool IsEnabled(LogLevel level);
}
