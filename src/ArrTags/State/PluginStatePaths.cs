using System;
using System.Globalization;
using System.IO;

namespace ArrTags.State;

/// <summary>
/// Resolves safe state paths under the plugin data folder. Record kinds and
/// identifiers are validated as single path segments so path traversal cannot
/// escape the plugin-owned state root.
/// </summary>
public sealed class PluginStatePaths
{
    private const string CacheDirectoryName = "cache";
    private const string AuthoritativeDirectoryName = "authoritative";
    private const string QuarantineDirectoryName = "quarantine";

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginStatePaths"/> class.
    /// </summary>
    /// <param name="rootPath">The plugin-owned state root.</param>
    public PluginStatePaths(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        _root = Path.GetFullPath(rootPath);
    }

    /// <summary>
    /// Gets the absolute state root.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Gets the absolute directory for a state authority.
    /// </summary>
    /// <param name="authority">The state authority.</param>
    /// <returns>The absolute directory path.</returns>
    public string GetDirectory(StateAuthority authority)
    {
        return Path.Combine(_root, authority == StateAuthority.Cache ? CacheDirectoryName : AuthoritativeDirectoryName);
    }

    /// <summary>
    /// Gets the absolute record path for a kind and identifier.
    /// </summary>
    /// <param name="authority">The state authority.</param>
    /// <param name="kind">The record kind.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <returns>The absolute record path.</returns>
    public string GetRecordPath(StateAuthority authority, string kind, string recordId)
    {
        ValidateSegment(kind, nameof(kind));
        ValidateSegment(recordId, nameof(recordId));
        return Path.Combine(GetDirectory(authority), kind, string.Concat(recordId, ".json"));
    }

    /// <summary>
    /// Gets the absolute quarantine path for a kind, identifier, and timestamp.
    /// </summary>
    /// <param name="authority">The state authority.</param>
    /// <param name="kind">The record kind.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="timestamp">The quarantine timestamp.</param>
    /// <returns>The absolute quarantine path.</returns>
    public string GetQuarantinePath(StateAuthority authority, string kind, string recordId, DateTimeOffset timestamp)
    {
        ValidateSegment(kind, nameof(kind));
        ValidateSegment(recordId, nameof(recordId));

        var stamp = timestamp.UtcDateTime.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        return Path.Combine(
            _root,
            QuarantineDirectoryName,
            authority == StateAuthority.Cache ? CacheDirectoryName : AuthoritativeDirectoryName,
            kind,
            string.Concat(recordId, ".", stamp, ".json"));
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 200
            || value.Contains("..", StringComparison.Ordinal)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || Path.IsPathRooted(value))
        {
            throw new ArgumentException("State record identifiers must be safe single path segments.", parameterName);
        }
    }
}
