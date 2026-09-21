using System;
using System.Collections.Generic;
using System.IO;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// The bounded authoritative artifact retention/garbage collector. It reclaims
/// artifact bytes (retained source baselines and durable derived render output)
/// only after proving that a candidate artifact is not the active image, not the
/// retained source of a live ownership session, and not referenced by a
/// non-terminal or recovery-blocked artwork operation.
/// </summary>
/// <remarks>
/// Artifacts are authoritative storage, never ordinary cache: render work-cache
/// retention never touches them, and this policy never evicts an active, owned,
/// or recovery-relevant artifact. The proof is fail-closed: if any
/// authoritative state or operation record cannot be validated, the whole pass
/// is skipped rather than deleting bytes a corrupt record might still reference.
/// Superseded derived render output that is no longer the active image is
/// reclaimed so the authoritative quota can be reused, while the retained source
/// baseline of a live session is preserved. The policy performs no image
/// mutation and no Jellyfin call.
/// </remarks>
public sealed class ArtifactRetention
{
    /// <summary>
    /// The bounded maximum number of authoritative records examined per pass.
    /// </summary>
    public const int MaxRecords = StateRepository.MaxEnumerationRecords;

    /// <summary>
    /// The default bounded grace period before a just-promoted artifact may be
    /// reclaimed. It closes the window in which a publication has written an
    /// artifact but has not yet made a durable reference to it.
    /// </summary>
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(1);

    private readonly StateRepository _repository;
    private readonly PublishedArtworkStateStore _states;
    private readonly ArtworkOperationStore _operations;
    private readonly SourceArtifactStore _artifacts;
    private readonly TimeSpan _minimumAge;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactRetention"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository that owns the plugin data root and limits.</param>
    /// <param name="artifacts">The authoritative artifact store.</param>
    /// <param name="minimumAge">An optional bounded artifact grace period; defaults to <see cref="DefaultMinimumAge"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtifactRetention(StateRepository repository, SourceArtifactStore artifacts, TimeSpan? minimumAge = null)
        : this(repository, new PublishedArtworkStateStore(repository), new ArtworkOperationStore(repository), artifacts, minimumAge)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactRetention"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository that owns the plugin data root and limits.</param>
    /// <param name="states">The authoritative published-artwork state store.</param>
    /// <param name="operations">The authoritative artwork operation store.</param>
    /// <param name="artifacts">The authoritative artifact store.</param>
    /// <param name="minimumAge">An optional bounded artifact grace period; defaults to <see cref="DefaultMinimumAge"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ArtifactRetention(
        StateRepository repository,
        PublishedArtworkStateStore states,
        ArtworkOperationStore operations,
        SourceArtifactStore artifacts,
        TimeSpan? minimumAge = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _minimumAge = minimumAge is { } value
            ? value < TimeSpan.Zero ? TimeSpan.Zero : value
            : DefaultMinimumAge;
    }

    /// <summary>
    /// Applies bounded artifact retention at a point in time. Only artifacts
    /// proven non-active and not referenced by a live session or a non-terminal
    /// operation are removed. The pass is fail-closed when an authoritative
    /// record cannot be validated.
    /// </summary>
    /// <remarks>
    /// Because the artifact store is content-addressed and idempotent, a
    /// concurrent publication can reuse an existing artifact's exact bytes
    /// without rewriting them, so the just-promoted grace period alone does not
    /// cover a re-reference. Immediately before each delete the policy re-checks
    /// whether any authoritative reference record changed since the reference
    /// snapshot and, if so, rebuilds the protected set from the latest records.
    /// The check is a bounded, constant-cost directory-generation comparison, so
    /// the common case performs no extra record reads. The residual window
    /// between the re-check and the delete is not a distributed lock; a
    /// reference made durable in that interval is still possible, which is why
    /// the grace period and the next pass provide the outer safety margin.
    /// </remarks>
    /// <param name="now">The current time.</param>
    /// <returns>The number of removed artifacts.</returns>
    public int Apply(DateTimeOffset now)
    {
        var retention = TimeSpan.FromDays(_repository.Limits.TerminalProvenanceRetentionDays);
        var referenceStamp = CaptureReferenceStamp();

        if (!TryCollectStates(out var states) || !TryCollectOperations(out var operations))
        {
            // Fail closed: an unreadable or invalid authoritative record might
            // reference an artifact that is otherwise unproven, so no artifact
            // may be deleted in this pass.
            return 0;
        }

        var protectedIds = BuildProtectedIds(states, operations, retention, now);

        var removed = 0;
        foreach (var artifactId in _artifacts.EnumerateArtifactIds(MaxRecords))
        {
            if (protectedIds.Contains(artifactId))
            {
                continue;
            }

            if (!_artifacts.TryGetArtifactLastWriteTime(artifactId, out var writtenAt))
            {
                continue;
            }

            // A just-promoted artifact may not yet be referenced by a durable
            // operation; the bounded grace period prevents reclaiming it before
            // the publication has made its reference durable.
            if (writtenAt + _minimumAge > now)
            {
                continue;
            }

            var currentStamp = CaptureReferenceStamp();
            if (currentStamp != referenceStamp)
            {
                // Re-check immediately before the delete: an authoritative
                // reference written after the snapshot must be observed so the
                // re-referenced artifact is not removed. The generation is
                // captured before the re-read, so a write during the re-read
                // leaves the stamp stale and triggers another re-check.
                referenceStamp = currentStamp;
                if (!TryCollectStates(out states) || !TryCollectOperations(out operations))
                {
                    // Fail closed: safety cannot be proven, so stop deleting.
                    break;
                }

                protectedIds = BuildProtectedIds(states, operations, retention, now);
                if (protectedIds.Contains(artifactId))
                {
                    continue;
                }
            }

            if (_artifacts.Delete(artifactId))
            {
                removed++;
            }
        }

        return removed;
    }

    private HashSet<string> BuildProtectedIds(
        IReadOnlyList<PublishedArtworkState> states,
        IReadOnlyList<ArtworkOperation> operations,
        TimeSpan retention,
        DateTimeOffset now)
    {
        var protectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var state in states)
        {
            var terminal = state.IsTerminal;
            var withinRetention = !terminal || state.UpdatedAt + retention >= now;

            if (withinRetention && state.SourcePresence == ArtworkImagePresence.Present)
            {
                // A live session's retained source baseline is never evicted as
                // ordinary cache; a terminal session's source is kept until the
                // bounded terminal-provenance window ends.
                Add(protectedIds, state.SourceArtifactId);
            }

            if (!terminal
                && state.State is ArtworkPublicationState.Published or ArtworkPublicationState.RestorePending)
            {
                // The currently active ArrTags-derived image is not garbage.
                Add(protectedIds, state.ActiveImageIdentity?.ContentSha256);
            }
        }

        foreach (var operation in operations)
        {
            if (!IsSettled(operation.Phase))
            {
                // A non-terminal or recovery-blocked operation may still need
                // its source, derived, and candidate artifacts to complete or to
                // reconcile its external postcondition.
                Add(protectedIds, operation.SourceArtifactId);
                Add(protectedIds, operation.DerivedArtifactId);
                Add(protectedIds, operation.CandidateAfterContentSha256);
                continue;
            }

            if (operation.UpdatedAt + retention >= now)
            {
                // Terminal provenance keeps its source baseline for the bounded
                // retention window. Superseded derived render output is not
                // protected, so the authoritative quota can be reclaimed.
                Add(protectedIds, operation.SourceArtifactId);
            }
        }

        return protectedIds;
    }

    /// <summary>
    /// Captures the current generation of the authoritative reference-record
    /// directories. A write to any state or operation record updates its kind
    /// directory's modification time, so comparing this stamp before a delete is
    /// a bounded, constant-cost way to detect that the reference snapshot may be
    /// stale.
    /// </summary>
    private (DateTime States, DateTime Operations) CaptureReferenceStamp()
    {
        return (
            GetKindDirectoryWriteTime(PublishedArtworkStateStore.RecordKind),
            GetKindDirectoryWriteTime(ArtworkOperationStore.RecordKind));
    }

    private DateTime GetKindDirectoryWriteTime(string kind)
    {
        try
        {
            var directory = _repository.Paths.GetKindDirectory(StateAuthority.Authoritative, kind);
            return Directory.Exists(directory) ? Directory.GetLastWriteTimeUtc(directory) : DateTime.MinValue;
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Determines whether an operation is settled and no longer needs its derived
    /// render output. <see cref="ArtworkOperationPhase.RecoveryBlocked"/> is
    /// deliberately not settled: it is terminal for the phase machine but may
    /// still require its artifacts for explicit later reconciliation.
    /// </summary>
    private static bool IsSettled(ArtworkOperationPhase phase)
    {
        return phase is ArtworkOperationPhase.Committed or ArtworkOperationPhase.Aborted;
    }

    private static void Add(HashSet<string> protectedIds, string? artifactId)
    {
        if (SourceArtifactPaths.IsValidArtifactId(artifactId))
        {
            protectedIds.Add(SourceArtifactPaths.NormalizeArtifactId(artifactId!));
        }
    }

    private bool TryCollectStates(out List<PublishedArtworkState> states)
    {
        states = new List<PublishedArtworkState>();
        return TryCollect(
            PublishedArtworkStateStore.RecordKind,
            _states.Read,
            states);
    }

    private bool TryCollectOperations(out List<ArtworkOperation> operations)
    {
        operations = new List<ArtworkOperation>();
        return TryCollect(
            ArtworkOperationStore.RecordKind,
            _operations.Read,
            operations);
    }

    private bool TryCollect<T>(string kind, Func<string, StateReadResult<T>> read, List<T> results)
        where T : class
    {
        var directory = _repository.Paths.GetKindDirectory(StateAuthority.Authoritative, kind);
        if (!Directory.Exists(directory))
        {
            return true;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (files.Length > MaxRecords)
        {
            // A bounded pass cannot prove safety when it cannot examine every
            // record; fail closed rather than delete an unexamined reference.
            return false;
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var recordId = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(recordId))
            {
                continue;
            }

            StateReadResult<T> result;
            try
            {
                result = read(recordId);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
            }

            if (result.Status is StateReadStatus.InvalidQuarantined or StateReadStatus.InvalidDiscarded)
            {
                return false;
            }

            if (result.Status == StateReadStatus.Found && result.Value is not null)
            {
                results.Add(result.Value);
            }
        }

        return true;
    }
}
