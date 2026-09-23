using Microsoft.Extensions.Logging;

namespace ArrTags.Logging;

/// <summary>
/// The plugin-owned logging boundary (ADR-020). It gates emission on the current
/// plugin verbosity (ADR-020 clause 3), applies the bounded repetition
/// suppression (ADR-020 clause 6), and writes only bounded, already-redacted
/// values to the host <see cref="ILogger"/> (ADR-020 clause 4). Callers supply
/// the final, secret-free message text; this boundary never adds a raw request
/// or response body, a header, a credential, a provider payload, or the mutable
/// <c>PluginConfiguration</c>.
/// </summary>
public interface IArrTagsLog
{
    /// <summary>
    /// Returns whether the supplied level is currently emitted for this category.
    /// </summary>
    /// <param name="level">The candidate log level.</param>
    /// <returns><see langword="true"/> when a record would be written; otherwise <see langword="false"/>.</returns>
    bool IsEnabled(LogLevel level);

    /// <summary>
    /// Writes one bounded, secret-free message when the level is enabled and the
    /// repetition-suppression bound admits it.
    /// </summary>
    /// <param name="level">The level to emit at.</param>
    /// <param name="logEvent">The bounded, code-owned event identity.</param>
    /// <param name="message">The final, bounded, secret-free message text.</param>
    void Write(LogLevel level, ArrTagsLogEvent logEvent, string message);
}

/// <summary>
/// The category-scoped logging boundary. <typeparamref name="T"/> is the calling
/// type, so the host logger category is the calling type's name and every
/// ArrTags category is prefixed <c>ArrTags.*</c> (ADR-020 clause 1).
/// </summary>
/// <typeparam name="T">The calling boundary type that owns the log category.</typeparam>
public interface IArrTagsLog<T> : IArrTagsLog
{
}
