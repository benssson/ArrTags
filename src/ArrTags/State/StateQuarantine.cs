using System;
using System.IO;
using System.Text;

namespace ArrTags.State;

/// <summary>
/// Moves invalid authoritative records to a quarantine area while preserving
/// their bytes. Quarantine never deletes an authoritative record; a failed
/// quarantine leaves the original file in place.
/// </summary>
public static class StateQuarantine
{
    /// <summary>
    /// Attempts to quarantine an invalid authoritative state record.
    /// </summary>
    /// <param name="paths">The safe state paths.</param>
    /// <param name="authority">The state authority.</param>
    /// <param name="kind">The record kind.</param>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="timestamp">The quarantine timestamp.</param>
    /// <param name="reason">A bounded, non-secret explanation.</param>
    /// <param name="quarantinePath">The destination path when successful.</param>
    /// <returns><see langword="true"/> when the record was quarantined.</returns>
    public static bool TryQuarantine(
        PluginStatePaths paths,
        StateAuthority authority,
        string kind,
        string recordId,
        DateTimeOffset timestamp,
        string reason,
        out string quarantinePath)
    {
        ArgumentNullException.ThrowIfNull(paths);

        quarantinePath = paths.GetQuarantinePath(authority, kind, recordId, timestamp);
        var source = paths.GetRecordPath(authority, kind, recordId);

        try
        {
            var directory = Path.GetDirectoryName(quarantinePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(source, quarantinePath, overwrite: true);
            TryWriteReason(quarantinePath, reason);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryWriteReason(string quarantinePath, string reason)
    {
        try
        {
            var bounded = reason.Length > 512 ? reason[..512] : reason;
            AtomicFileWriter.Write(string.Concat(quarantinePath, ".reason.txt"), Encoding.UTF8.GetBytes(bounded));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
