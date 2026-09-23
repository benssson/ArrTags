using System;
using ArrTags.Configuration;
using Microsoft.Extensions.Logging;

namespace ArrTags.PluginLifecycle;

/// <summary>
/// The default <see cref="ILogVerbosityGate"/>. It maps the bounded
/// <see cref="LogVerbosity"/> to a <see cref="LogLevel"/> and reads the current
/// configuration snapshot on every call, so a replacement takes effect without
/// rebuilding the singleton. An undefined verbosity (which validation rejects)
/// falls back to the safe <see cref="LogVerbosity.Warning"/> default, so it can
/// never raise verbosity.
/// </summary>
public sealed class LogVerbosityGate : ILogVerbosityGate
{
    private readonly ConfigurationSnapshotService _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogVerbosityGate"/> class.
    /// </summary>
    /// <param name="configuration">The configuration snapshot service that owns the active verbosity.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public LogVerbosityGate(ConfigurationSnapshotService configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <inheritdoc />
    public LogLevel EffectiveLevel => ToLogLevel(_configuration.Current.LogVerbosity);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level)
    {
        if (level == LogLevel.None)
        {
            return false;
        }

        var minimum = EffectiveLevel;
        return minimum != LogLevel.None && level >= minimum;
    }

    /// <summary>
    /// Maps the bounded plugin verbosity to the matching
    /// <see cref="LogLevel"/>. An undefined value falls back to
    /// <see cref="LogLevel.Warning"/>.
    /// </summary>
    /// <param name="verbosity">The configured plugin verbosity.</param>
    /// <returns>The minimum enabled host log level.</returns>
    public static LogLevel ToLogLevel(LogVerbosity verbosity)
    {
        return verbosity switch
        {
            LogVerbosity.Off => LogLevel.None,
            LogVerbosity.Error => LogLevel.Error,
            LogVerbosity.Warning => LogLevel.Warning,
            LogVerbosity.Information => LogLevel.Information,
            LogVerbosity.Debug => LogLevel.Debug,
            LogVerbosity.Trace => LogLevel.Trace,
            _ => LogLevel.Warning,
        };
    }
}
