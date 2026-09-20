using System;
using System.IO;
using ArrTags.State;

namespace ArrTags.Artwork;

/// <summary>
/// Persists and reads the durable active <see cref="ArtworkLifecycleFence"/>
/// through the versioned authoritative state boundary. The fence is
/// integrity-tagged and atomically written; a missing record is a normal fence,
/// while an invalid record is preserved as fail-closed (it is never quarantined
/// away or overwritten with normal) so the runtime publication path stays
/// closed until an explicit recovery decision. The fence survives a crash or
/// restart so a disable or uninstall in progress cannot be observed as a normal
/// publication window.
/// </summary>
public sealed class ArtworkLifecycleFenceStore
{
    /// <summary>
    /// The authoritative state record kind for the active lifecycle fence.
    /// </summary>
    public const string RecordKind = "artwork-lifecycle-fence";

    /// <summary>
    /// The single record identifier for the active fence.
    /// </summary>
    public const string ActiveRecordId = "active";

    private readonly StateRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkLifecycleFenceStore"/> class.
    /// </summary>
    /// <param name="repository">The versioned state repository under the plugin data root.</param>
    /// <exception cref="ArgumentNullException">The repository is <see langword="null"/>.</exception>
    public ArtworkLifecycleFenceStore(StateRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>
    /// Reads the durable active fence. A missing record is normal; an invalid
    /// record is reported as fail-closed and is intentionally left in place so
    /// the publication read path remains closed rather than treating the invalid
    /// record as absent.
    /// </summary>
    /// <returns>The bounded active-fence state.</returns>
    public ArtworkLifecycleFenceState Read()
    {
        var path = _repository.Paths.GetRecordPath(StateAuthority.Authoritative, RecordKind, ActiveRecordId);
        if (!File.Exists(path))
        {
            return ArtworkLifecycleFenceState.Normal;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ArtworkLifecycleFenceState.Invalid("The lifecycle-fence record could not be read.");
        }

        if (!StateEnvelopeCodec.TryDeserialize<ArtworkLifecycleFenceRecord>(bytes, out _, out var payload, out var reason)
            || payload is null)
        {
            return ArtworkLifecycleFenceState.Invalid(reason);
        }

        if (!payload.Validate(out var validationReason))
        {
            return ArtworkLifecycleFenceState.Invalid(validationReason);
        }

        return ArtworkLifecycleFenceState.Create(payload.Fence, payload.Reason);
    }

    /// <summary>
    /// Writes the active fence atomically as authoritative state. The record is
    /// never terminal, so it is never pruned as ordinary cache.
    /// </summary>
    /// <param name="fence">The active fence.</param>
    /// <param name="reason">An optional bounded, non-secret explanation.</param>
    /// <exception cref="ArgumentOutOfRangeException">The fence is undefined.</exception>
    public void Set(ArtworkLifecycleFence fence, string? reason = null)
    {
        var record = new ArtworkLifecycleFenceRecord(fence, DateTimeOffset.UtcNow, reason: reason);
        _repository.Write(StateAuthority.Authoritative, RecordKind, ActiveRecordId, record);
    }

    /// <summary>
    /// Clears a valid stale fence to normal when the host has loaded the plugin
    /// active. A missing or already-normal fence is left untouched, and an
    /// invalid fence is preserved as fail-closed rather than silently overwritten
    /// with normal; clearing an invalid fence requires an explicit recovery
    /// decision that this store does not make.
    /// </summary>
    public void Reset()
    {
        var current = Read();
        if (!current.IsValid || current.Fence == ArtworkLifecycleFence.Normal)
        {
            return;
        }

        Set(ArtworkLifecycleFence.Normal, "The host loaded the plugin active; the valid pending disable or uninstall fence is cleared.");
    }
}
