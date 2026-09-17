using System;
using System.Collections.Generic;

namespace ArrTags.Configuration;

/// <summary>
/// Describes the outcome of validating a candidate <see cref="PluginConfiguration"/>.
/// </summary>
public sealed class ConfigurationValidationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationValidationResult"/> class.
    /// </summary>
    /// <param name="errors">The safe, secret-free validation messages.</param>
    public ConfigurationValidationResult(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    /// <summary>
    /// Gets the safe, secret-free validation messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the configuration is valid.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}
