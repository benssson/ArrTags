using System;
using System.IO;
using ArrTags.Configuration;

namespace ArrTags.State;

/// <summary>
/// Reads and writes versioned plugin state under a plugin-owned state root.
/// Invalid cache records are discarded so they cannot block startup; invalid
/// authoritative records are quarantined and never treated as absent.
/// </summary>
public sealed class StateRepository
{
    /// <summary>
    /// The maximum permitted encoded state record size in bytes. State records
    /// hold bounded metadata and references, not image artifacts.
    /// </summary>
    public const int MaxStateRecordBytes = 4 * 1024 * 1024;

    private readonly PluginStatePaths _paths;
    private readonly OperationalLimits _limits;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateRepository"/> class.
    /// </summary>
    /// <param name="rootPath">The plugin-owned state root.</param>
    /// <param name="limits">The operational limits governing retention. Defaults are used when omitted.</param>
    public StateRepository(string rootPath, OperationalLimits? limits = null)
    {
        _paths = new PluginStatePaths(rootPath);
        _limits = limits ?? new OperationalLimits();
    }

    /// <summary>
    /// Gets the safe state paths.
    /// </summary>
    public PluginStatePaths Paths => _paths;

    /// <summary>
    /// Gets the operational limits governing retention and authoritative artifact storage.
    /// </summary>
    public OperationalLimits Limits => _limits;

    /// <summary>
    /// Reads a state record.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="authority">The state authority.</param>
    /// <param name="kind">The record kind.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <returns>The read outcome.</returns>
    public StateReadResult<T> Read<T>(StateAuthority authority, string kind, string recordId)
        where T : class
    {
        var path = _paths.GetRecordPath(authority, kind, recordId);
        if (!File.Exists(path))
        {
            return StateResults.Missing<T>();
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HandleInvalid<T>(authority, kind, recordId, "The state record could not be read.");
        }

        if (!StateEnvelopeCodec.TryDeserialize<T>(bytes, out _, out var payload, out var reason))
        {
            return HandleInvalid<T>(authority, kind, recordId, reason);
        }

        return StateResults.Found(payload!);
    }

    /// <summary>
    /// Writes a state record atomically.
    /// </summary>
    /// <typeparam name="T">The record payload type.</typeparam>
    /// <param name="authority">The state authority.</param>
    /// <param name="kind">The record kind.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="terminal">Whether the record is terminal and eligible for retention cleanup.</param>
    public void Write<T>(StateAuthority authority, string kind, string recordId, T payload, bool terminal = false)
        where T : class
    {
        var bytes = StateEnvelopeCodec.Serialize(authority, kind, recordId, payload, DateTimeOffset.UtcNow, terminal);
        if (bytes.Length > MaxStateRecordBytes)
        {
            throw new InvalidOperationException("The state record exceeds the bounded maximum size.");
        }

        AtomicFileWriter.Write(_paths.GetRecordPath(authority, kind, recordId), bytes);
    }

    /// <summary>
    /// Applies the configured cache retention and terminal-provenance retention
    /// policies. Non-terminal authoritative state is never pruned.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The number of removed records.</returns>
    public int ApplyRetention(DateTimeOffset now)
    {
        var cacheRemoved = StateRetention.ApplyCacheRetention(
            _paths,
            TimeSpan.FromMinutes(_limits.RenderCacheTtlMinutes),
            _limits.RenderCacheQuotaBytes,
            now);

        var authoritativeRemoved = StateRetention.ApplyAuthoritativeRetention(
            _paths,
            TimeSpan.FromDays(_limits.TerminalProvenanceRetentionDays),
            now);

        return cacheRemoved + authoritativeRemoved;
    }

    private StateReadResult<T> HandleInvalid<T>(StateAuthority authority, string kind, string recordId, string reason)
        where T : class
    {
        if (authority == StateAuthority.Cache)
        {
            TryDeleteRecord(kind, recordId);
            return StateResults.Discarded<T>(reason);
        }

        StateQuarantine.TryQuarantine(_paths, authority, kind, recordId, DateTimeOffset.UtcNow, reason, out _);
        return StateResults.Quarantined<T>(reason);
    }

    private void TryDeleteRecord(string kind, string recordId)
    {
        try
        {
            var path = _paths.GetRecordPath(StateAuthority.Cache, kind, recordId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
